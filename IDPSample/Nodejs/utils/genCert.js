// utils/genCert.js
const fs = require('fs');
const path = require('path');
const selfsigned = require('selfsigned');

const attrs = [{ name: 'commonName', value: 'localhost' }];
const pems = selfsigned.generate(attrs, {
  days: 365,
  keySize: 2048,
  algorithm: 'sha256',
});

const certDir = path.join(__dirname, 'certs');
fs.mkdirSync(certDir, { recursive: true });

fs.writeFileSync(path.join(certDir, 'key.pem'),  pems.private);
fs.writeFileSync(path.join(certDir, 'cert.pem'), pems.cert);

console.log('생성 완료:', path.join(certDir, 'key.pem'), path.join(certDir, 'cert.pem'));
