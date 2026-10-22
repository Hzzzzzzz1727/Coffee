const { sql, connection } = require('./db')
const areas = ['Khu vực trong nhà']
const tables = [['Bàn 01', 2], ['Bàn 02', 2], ['Bàn 03', 4], ['Bàn 04', 4], ['Bàn 05', 2], ['Bàn 06', 4], ['Bàn 07', 6], ['Bàn 08', 2]]
const products = [
  ['Cà phê', 'Americano', 39000], ['Cà phê', 'Latte hạt dẻ', 55000], ['Cà phê', 'Cold brew cam', 59000],
  ['Trà & matcha', 'Matcha cloud', 62000], ['Trà & matcha', 'Trà đào cam sả', 49000], ['Bánh', 'Croissant bơ', 35000]
]
async function run() {
  const pool = await connection
  const store = (await pool.request().query('SELECT TOP 1 Id FROM dbo.Stores ORDER BY CreatedAt')).recordset[0]
  if (!store) throw new Error('Hãy chạy migration trước.')
  for (const [name, position] of areas.entries()) await pool.request().input('storeId', sql.UniqueIdentifier, store.Id).input('name', sql.NVarChar, name).input('pos', sql.Int, position).query("IF NOT EXISTS (SELECT 1 FROM dbo.Areas WHERE StoreId=@storeId AND Name=@name) INSERT dbo.Areas(StoreId,Name,Position) VALUES(@storeId,@name,@pos)")
  const area = (await pool.request().input('storeId', sql.UniqueIdentifier, store.Id).query('SELECT TOP 1 Id FROM dbo.Areas WHERE StoreId=@storeId ORDER BY Position')).recordset[0]
  for (const [code, seats] of tables) await pool.request().input('areaId', sql.UniqueIdentifier, area.Id).input('code', sql.NVarChar, code).input('seats', sql.TinyInt, seats).query("IF NOT EXISTS (SELECT 1 FROM dbo.DiningTables WHERE AreaId=@areaId AND Code=@code) INSERT dbo.DiningTables(AreaId,Code,Seats) VALUES(@areaId,@code,@seats)")
  for (const category of [...new Set(products.map(p => p[0]))]) await pool.request().input('storeId', sql.UniqueIdentifier, store.Id).input('name', sql.NVarChar, category).query("IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE StoreId=@storeId AND Name=@name) INSERT dbo.Categories(StoreId,Name) VALUES(@storeId,@name)")
  for (const [category, name, price] of products) await pool.request().input('storeId', sql.UniqueIdentifier, store.Id).input('category', sql.NVarChar, category).input('name', sql.NVarChar, name).input('price', sql.Int, price).query("IF NOT EXISTS (SELECT 1 FROM dbo.Products p JOIN dbo.Categories c ON c.Id=p.CategoryId WHERE c.StoreId=@storeId AND p.Name=@name) INSERT dbo.Products(CategoryId,Name,BasePrice) SELECT TOP 1 Id,@name,@price FROM dbo.Categories WHERE StoreId=@storeId AND Name=@category")
  await pool.close(); console.log('Đã tạo bàn và menu khởi đầu.')
}
run().catch(err => { console.error(err.message); process.exit(1) })
