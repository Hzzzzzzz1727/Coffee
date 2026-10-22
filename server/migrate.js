const fs = require('fs')
const path = require('path')
const { sql, connection } = require('./db')

async function run() {
  const pool = await connection
  await pool.request().query(`IF OBJECT_ID('dbo.SchemaMigrations', 'U') IS NULL CREATE TABLE dbo.SchemaMigrations (Name nvarchar(255) NOT NULL PRIMARY KEY, AppliedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME())`)
  const files = fs.readdirSync(path.join(__dirname, 'migrations')).filter(f => f.endsWith('.sql')).sort()
  for (const name of files) {
    const exists = await pool.request().input('name', sql.NVarChar, name).query('SELECT 1 AS present FROM dbo.SchemaMigrations WHERE Name=@name')
    if (!exists.recordset.length) {
      const batches = fs.readFileSync(path.join(__dirname, 'migrations', name), 'utf8').split(/^\s*GO\s*$/gim).filter(batch => batch.trim())
      for (const batch of batches) await pool.request().batch(batch)
      await pool.request().input('name', sql.NVarChar, name).query('INSERT dbo.SchemaMigrations(Name) VALUES(@name)')
      console.log(`Applied ${name}`)
    }
  }
  await pool.close()
}
run().catch(err => { console.error(err); process.exit(1) })
