// Tiny fixture used for manual smoke tests. Start it with an absolute path so
// PortDeck can infer this folder as the owning project:
//   node D:\path\to\portdeck\tests\fixtures\demo-app\server.js 34567
const http = require('node:http');

const port = Number(process.argv[2] || 34567);

http
  .createServer((request, response) => {
    response.writeHead(200, { 'content-type': 'text/plain; charset=utf-8' });
    response.end('PortDeck demo service is running.\n');
  })
  .listen(port, '127.0.0.1', () => {
    console.log(`PortDeck demo service listening on http://127.0.0.1:${port}`);
  });
