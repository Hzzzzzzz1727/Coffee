const bcrypt = require('bcryptjs')
const { sql, connection } = require('./db')
async function run() {
  const [email, password, name = 'Chủ cửa hàng'] = process.argv.slice(2)
  if (!email || !password) throw new Error('Dùng: node server/seed-admin.js email password [name]')
  const pool = await connection; const store = await pool.request().query('SELECT TOP 1 Id FROM dbo.Stores ORDER BY CreatedAt')
  if (!store.recordset[0]) throw new Error('Chưa có cửa hàng. Hãy chạy migration trước.')
  const hash = await bcrypt.hash(password, 12)
  await pool.request().input('storeId',sql.UniqueIdentifier,store.recordset[0].Id).input('email',sql.NVarChar,email.toLowerCase()).input('name',sql.NVarChar,name).input('hash',sql.NVarChar,hash).query("INSERT dbo.Users(StoreId,FullName,Email,PasswordHash,Role) VALUES(@storeId,@name,@email,@hash,'owner')")
  await pool.close(); console.log('Đã tạo tài khoản owner.')
}
run().catch(err => { console.error(err.message); process.exit(1) })
