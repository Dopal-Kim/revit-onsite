// usage: node rv.js <file.cs> [auto|none] ['<json params array>']  — sends C# to Revit (port 8080) directly
const net = require('net'), fs = require('fs');
const code = fs.readFileSync(process.argv[2], 'utf8');
const req = { jsonrpc: '2.0', method: 'send_code_to_revit', id: 'c' + Date.now(),
  params: { code, parameters: JSON.parse(process.argv[4] || '[]'), transactionMode: process.argv[3] || 'auto' } };
const s = net.connect(8080, '127.0.0.1', () => s.write(JSON.stringify(req)));
let buf = '';
s.on('data', d => { buf += d; try { const r = JSON.parse(buf); const x = r.result || r.error;
  if (x && x.Result) { try { x.Result = JSON.parse(x.Result); } catch (e) {} }
  console.log(JSON.stringify(x, null, 1)); s.end(); process.exit(0); } catch (e) {} });
s.on('error', e => { console.log('SOCKET ERROR', e.message); process.exit(1); });
setTimeout(() => { console.log('TIMEOUT 150s'); process.exit(1); }, 150000);
