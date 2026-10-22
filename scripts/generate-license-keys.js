const crypto = require('crypto')
const fs = require('fs')
const path = require('path')

const root = path.resolve(__dirname, '..')
const privatePath = path.join(root, 'vendor-tools', 'license-private.pem')
const publicPath = path.join(root, 'server-dotnet', 'license-public.pem')

if (fs.existsSync(privatePath) || fs.existsSync(publicPath)) {
  console.error('Khóa đã tồn tại. Không ghi đè để tránh làm vô hiệu các license đã bán.')
  process.exit(1)
}

const { privateKey, publicKey } = crypto.generateKeyPairSync('rsa', {
  modulusLength: 3072,
  publicKeyEncoding: { type: 'spki', format: 'pem' },
  privateKeyEncoding: { type: 'pkcs8', format: 'pem' }
})

fs.mkdirSync(path.dirname(privatePath), { recursive: true })
fs.writeFileSync(privatePath, privateKey, { mode: 0o600 })
fs.writeFileSync(publicPath, publicKey)
console.log('Đã tạo cặp khóa. Giữ kín vendor-tools/license-private.pem và không gửi file này cho khách.')
