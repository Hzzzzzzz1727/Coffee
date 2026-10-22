const crypto = require('crypto')
const fs = require('fs')
const path = require('path')

const [, , installationId, licensedTo, expiry = 'never'] = process.argv
if (!installationId || !licensedTo) {
  console.error('Cách dùng: npm run license:create -- <MA_MAY> "Tên khách hàng" [YYYY-MM-DD|never]')
  process.exit(1)
}

const privatePath = path.resolve(__dirname, '..', 'vendor-tools', 'license-private.pem')
if (!fs.existsSync(privatePath)) {
  console.error('Không tìm thấy private key. Chạy npm run license:init một lần trên máy của bạn.')
  process.exit(1)
}

let expiresAt = null
if (expiry.toLowerCase() !== 'never') {
  const date = new Date(`${expiry}T23:59:59.999Z`)
  if (Number.isNaN(date.getTime())) {
    console.error('Ngày hết hạn phải có dạng YYYY-MM-DD hoặc never.')
    process.exit(1)
  }
  expiresAt = date.toISOString()
}

const payload = Buffer.from(JSON.stringify({
  version: 1,
  installationId: installationId.trim().toUpperCase(),
  licensedTo: licensedTo.trim(),
  issuedAt: new Date().toISOString(),
  expiresAt
}))
const signature = crypto.sign('sha256', payload, fs.readFileSync(privatePath))
console.log(`${payload.toString('base64url')}.${signature.toString('base64url')}`)
