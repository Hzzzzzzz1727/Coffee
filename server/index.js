require('dotenv').config()
const express = require('express')
const cors = require('cors')
const bcrypt = require('bcryptjs')
const jwt = require('jsonwebtoken')
const { sql, connection } = require('./db')
const app = express()

app.use(cors({ origin: process.env.CLIENT_ORIGIN || 'http://localhost:5173' }))
app.use(express.json({ limit: '1mb' }))
const asyncRoute = handler => (req, res, next) => Promise.resolve(handler(req, res, next)).catch(next)
const db = async () => connection
const auth = roles => (req, res, next) => {
  try {
    const token = req.headers.authorization?.replace('Bearer ', '')
    const user = jwt.verify(token, process.env.JWT_SECRET)
    if (roles?.length && !roles.includes(user.role)) return res.status(403).json({ error: 'Không có quyền thực hiện thao tác này.' })
    req.user = user; next()
  } catch { res.status(401).json({ error: 'Phiên đăng nhập không hợp lệ.' }) }
}

app.get('/api/health', asyncRoute(async (req, res) => { await db(); res.json({ ok: true }) }))
app.post('/api/auth/login', asyncRoute(async (req, res) => {
  const pool = await db(); const result = await pool.request().input('email', sql.NVarChar, String(req.body.email || '').toLowerCase()).query('SELECT Id, StoreId, FullName, Email, Role, PasswordHash FROM dbo.Users WHERE Email=@email AND IsActive=1')
  const user = result.recordset[0]
  if (!user || !await bcrypt.compare(req.body.password || '', user.PasswordHash)) return res.status(401).json({ error: 'Email hoặc mật khẩu không đúng.' })
  const token = jwt.sign({ id:user.Id, storeId:user.StoreId, role:user.Role, name:user.FullName }, process.env.JWT_SECRET, { expiresIn:'8h' })
  res.json({ token, user:{ id:user.Id, name:user.FullName, role:user.Role } })
}))
app.get('/api/tables', auth(), asyncRoute(async (req, res) => {
  const pool = await db(); const result = await pool.request().input('storeId', sql.UniqueIdentifier, req.user.storeId).query(`SELECT t.Id,t.Code AS name,t.Seats AS seats,t.Status AS status,a.Name AS area, activeOrder.Total AS total,activeOrder.OpenedAt AS openedAt FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId OUTER APPLY (SELECT TOP 1 Total,OpenedAt FROM dbo.Orders WHERE TableId=t.Id AND Status NOT IN ('paid','cancelled') ORDER BY OpenedAt DESC) activeOrder WHERE a.StoreId=@storeId ORDER BY a.Position,t.Code`)
  res.json(result.recordset)
}))
app.get('/api/products', auth(), asyncRoute(async (req, res) => {
  const pool = await db(); const result = await pool.request().input('storeId', sql.UniqueIdentifier, req.user.storeId).query('SELECT p.Id,p.Name,p.BasePrice AS price,p.ImageUrl,c.Name AS category FROM dbo.Products p JOIN dbo.Categories c ON c.Id=p.CategoryId WHERE c.StoreId=@storeId AND p.IsActive=1 ORDER BY c.Position,p.Name')
  res.json(result.recordset)
}))
app.get('/api/orders/kds', auth(['owner','manager','barista','server']), asyncRoute(async (req, res) => {
  const pool = await db(); const result = await pool.request().input('storeId', sql.UniqueIdentifier, req.user.storeId).query(`SELECT o.Id,o.OrderNumber,o.Status,o.OpenedAt,t.Code AS tableName,COUNT(i.Id) AS itemCount FROM dbo.Orders o LEFT JOIN dbo.DiningTables t ON t.Id=o.TableId LEFT JOIN dbo.OrderItems i ON i.OrderId=o.Id WHERE o.StoreId=@storeId AND o.Status IN ('sent','preparing','ready') GROUP BY o.Id,o.OrderNumber,o.Status,o.OpenedAt,t.Code ORDER BY o.OpenedAt`)
  res.json(result.recordset)
}))
app.post('/api/orders', auth(['owner','manager','cashier','server']), asyncRoute(async (req, res) => {
  const { tableId, guestCount = 1, note, items } = req.body
  if (!tableId || !Array.isArray(items) || !items.length) return res.status(400).json({ error:'Bàn và ít nhất một món là bắt buộc.' })
  const pool = await db(); const tx = new sql.Transaction(pool); await tx.begin()
  try {
    let total = 0; const detailed = []
    for (const item of items) { const p = await new sql.Request(tx).input('id', sql.UniqueIdentifier, item.productId).query('SELECT Id,Name,BasePrice FROM dbo.Products WHERE Id=@id AND IsActive=1'); if (!p.recordset[0]) throw new Error('Món không còn khả dụng.'); const product=p.recordset[0]; const qty=Number(item.quantity); if (!Number.isInteger(qty)||qty<1) throw new Error('Số lượng món không hợp lệ.'); total+=product.BasePrice*qty; detailed.push({product,qty,note:item.note||null}) }
    const order = await new sql.Request(tx).input('storeId',sql.UniqueIdentifier,req.user.storeId).input('tableId',sql.UniqueIdentifier,tableId).input('guestCount',sql.TinyInt,guestCount).input('note',sql.NVarChar,note||null).input('total',sql.Int,total).input('userId',sql.UniqueIdentifier,req.user.id).query("INSERT dbo.Orders(StoreId,TableId,Status,GuestCount,Note,Subtotal,Total,OpenedBy) OUTPUT INSERTED.* VALUES(@storeId,@tableId,'sent',@guestCount,@note,@total,@total,@userId)")
    for (const item of detailed) await new sql.Request(tx).input('orderId',sql.UniqueIdentifier,order.recordset[0].Id).input('productId',sql.UniqueIdentifier,item.product.Id).input('name',sql.NVarChar,item.product.Name).input('price',sql.Int,item.product.BasePrice).input('qty',sql.SmallInt,item.qty).input('note',sql.NVarChar,item.note).query("INSERT dbo.OrderItems(OrderId,ProductId,ProductName,UnitPrice,Quantity,Note,Status) VALUES(@orderId,@productId,@name,@price,@qty,@note,'sent')")
    await new sql.Request(tx).input('tableId',sql.UniqueIdentifier,tableId).query("UPDATE dbo.DiningTables SET Status='occupied' WHERE Id=@tableId")
    await tx.commit(); res.status(201).json(order.recordset[0])
  } catch (error) { await tx.rollback(); throw error }
}))
app.patch('/api/orders/:id/status', auth(['owner','manager','cashier','server','barista']), asyncRoute(async (req, res) => {
  const allowed=['preparing','ready','served','paid','cancelled']; if(!allowed.includes(req.body.status)) return res.status(400).json({error:'Trạng thái không hợp lệ.'})
  const pool=await db(); const result=await pool.request().input('id',sql.UniqueIdentifier,req.params.id).input('storeId',sql.UniqueIdentifier,req.user.storeId).input('status',sql.NVarChar,req.body.status).query("UPDATE dbo.Orders SET Status=@status,ClosedAt=CASE WHEN @status='paid' THEN SYSUTCDATETIME() ELSE ClosedAt END OUTPUT INSERTED.* WHERE Id=@id AND StoreId=@storeId")
  if(!result.recordset[0]) return res.status(404).json({error:'Không tìm thấy đơn.'}); res.json(result.recordset[0])
}))
app.use((err, req, res, next) => { console.error(err); res.status(500).json({ error: err.message || 'Máy chủ gặp lỗi.' }) })
app.listen(process.env.PORT || 3031, () => console.log(`Coffee API running on port ${process.env.PORT || 3031}`))

