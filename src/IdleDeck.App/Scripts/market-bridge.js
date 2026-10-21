// Ponte do sniper com o jogo. Embrulha o WebSocket que o PRÓPRIO jogo abre (nada de segunda conexão) e:
//  - repassa ao app só o que o sniper precisa: respostas do mercado, eventos de compra e o saldo/relógio;
//  - deixa o app mandar pedidos do mercado (t que começa com "market.") pela mesma conexão e sessão.
// Nunca lê nem repassa token, cookie, chat nem o hello; não clica em nada.
(() => {
  if (window.__biSniper) return;
  const NativeWS = window.WebSocket;
  if (!NativeWS) return;

  let current = null;

  const post = (frame) => {
    try { window.chrome.webview.postMessage(JSON.stringify({ module: 'sniper', data: { frame } })); } catch (e) { /* sem canal com o app */ }
  };

  const relay = (raw) => {
    if (typeof raw !== 'string') return;
    const head = /^\s*\{\s*"t"\s*:\s*"([^"]+)"/.exec(raw);
    if (!head) return;
    const t = head[1];

    if (t === 'market') { post(raw); return; }

    if (t === 'batalha') {
      if (raw.indexOf('"mktSorteio"') < 0 && raw.indexOf('"marketComprado"') < 0) return;
      let data;
      try { data = JSON.parse(raw); } catch (e) { return; }
      const ev = Array.isArray(data.ev) ? data.ev.filter((e) => e && (e.k === 'mktSorteio' || e.k === 'marketComprado')) : [];
      if (ev.length) post(JSON.stringify({ t: 'batalha', ev }));
      return;
    }

    if (t === 'welcome' || t === 'estado') {
      let data;
      try { data = JSON.parse(raw); } catch (e) { return; }
      const frame = { t };
      const estado = data && data.estado;
      if (estado && typeof estado === 'object') {
        const out = {};
        for (const key of ['gold', 'orbs', 'servidorAgora'])
          if (typeof estado[key] === 'number') out[key] = estado[key];
        if (Object.keys(out).length) frame.estado = out;
      }
      // o catálogo de itens do mercado (casas, boxes, bicicletas…) vem só no welcome; leva só id, nome e categoria
      const catalogo = t === 'welcome' && data.mercado && Array.isArray(data.mercado.catalogo) ? data.mercado.catalogo : null;
      if (catalogo) {
        const items = [];
        for (const it of catalogo) {
          const name = it && (it.name || it.nome);
          if (it && typeof it.id === 'number' && typeof name === 'string')
            items.push({ id: it.id, name, category: String(it.category || it.categoria || '') });
        }
        if (items.length) frame.mercado = { catalogo: items };
      }
      if (frame.estado || frame.mercado) post(JSON.stringify(frame));
    }
  };

  function GameSocket(url, protocols) {
    const ws = protocols === undefined ? new NativeWS(url) : new NativeWS(url, protocols);
    if (String(url).toLowerCase().indexOf('pokeidle') >= 0) {
      current = ws;
      ws.addEventListener('message', (e) => relay(e.data));
      ws.addEventListener('close', () => { if (current === ws) current = null; });
    }
    return ws;
  }
  GameSocket.prototype = NativeWS.prototype;
  for (const name of ['CONNECTING', 'OPEN', 'CLOSING', 'CLOSED'])
    if (name in NativeWS) GameSocket[name] = NativeWS[name];
  window.WebSocket = GameSocket;

  window.__biSniper = {
    // Manda um pedido do mercado já em JSON (texto). False se não há soquete aberto ou se não é "market.*".
    send(text) {
      const ws = current;
      if (!ws || ws.readyState !== 1 || typeof text !== 'string') return false;
      let message;
      try { message = JSON.parse(text); } catch (e) { return false; }
      if (!message || typeof message.t !== 'string' || message.t.indexOf('market.') !== 0) return false;
      ws.send(text);
      return true;
    },
  };
})();
