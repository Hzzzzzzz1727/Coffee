require('dotenv').config()
const useWindowsAuth = process.env.DB_AUTH === 'windows'
const sql = useWindowsAuth ? require('mssql/msnodesqlv8') : require('mssql')

const required = useWindowsAuth ? ['DB_SERVER', 'DB_DATABASE'] : ['DB_SERVER', 'DB_DATABASE', 'DB_USER', 'DB_PASSWORD']
for (const key of required) if (!process.env[key]) throw new Error(`${key} is required. Copy .env.example to .env.`)

const config = {
  server: process.env.DB_SERVER,
  port: Number(process.env.DB_PORT || 1433),
  database: process.env.DB_DATABASE,
  ...(useWindowsAuth ? { driver: 'ODBC Driver 18 for SQL Server' } : { user: process.env.DB_USER, password: process.env.DB_PASSWORD }),
  options: { encrypt: process.env.DB_ENCRYPT === 'true', trustServerCertificate: process.env.DB_ENCRYPT !== 'true', ...(process.env.DB_INSTANCE ? { instanceName: process.env.DB_INSTANCE } : {}), ...(useWindowsAuth ? { trustedConnection: true } : {}) },
  pool: { max: 10, min: 0, idleTimeoutMillis: 30000 }
}
if (useWindowsAuth && process.env.DB_LOCAL_SHARED_MEMORY === 'true') config.connectionString = `Driver={ODBC Driver 18 for SQL Server};Server=lpc:(local);Database=${process.env.DB_DATABASE};Trusted_Connection=Yes;TrustServerCertificate=Yes;`
const pool = new sql.ConnectionPool(config)
const connection = pool.connect()
module.exports = { sql, pool, connection }
