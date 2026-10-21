// DIAGNÓSTICO (desligado por padrão): só descreve a FORMA do tráfego do jogo — método, caminho, status e o formato
// dos dados — para o BookIdle aprender como o mercado entrega as ofertas. Cabeçalhos e cookies nunca são lidos;
// chaves/tokens são mascarados AQUI, antes de qualquer coisa sair da página. Não altera nada, não clica, não compra.
(() => {
  if (window.__bookidleProbe) return;
  window.__bookidleProbe = true;
  const host = location.hostname.toLowerCase();
  if (!host.includes('pokeidle')) return;

  const SECRET_KEY = /token|auth|cookie|secret|pass|session|jwt|key|signature|bearer/i;
  const STATIC = /\.(js|mjs|css|png|jpe?g|gif|webp|svg|ico|woff2?|ttf|mp3|ogg|wav|map)(\?|$)|\/assets\/(asset-packs|world|index)\/|\/saude|\/cdn-cgi\//i;
  const seen = new Map();

  const send = (kind, text) => {
    try { window.chrome.webview.postMessage('probe:' + kind + ' ' + text.slice(0, 2400)); } catch (e) { /* sem canal */ }
  };

  const maskString = (s) => (s.length >= 28 && !/\s/.test(s) ? '<texto-longo:' + s.length + '>' : s.length > 120 ? s.slice(0, 120) + '…' : s);

  const shape = (v, depth) => {
    if (v === null || typeof v === 'boolean' || typeof v === 'number') return v;
    if (typeof v === 'string') return maskString(v);
    if (depth > 4) return '<fundo>';
    if (Array.isArray(v)) return { '<lista>': v.length, '<itens>': v.slice(0, 2).map((x) => shape(x, depth + 1)) };
    if (typeof v === 'object') {
      const out = {};
      for (const k of Object.keys(v).slice(0, 40)) out[k] = SECRET_KEY.test(k) ? '<mascarado>' : shape(v[k], depth + 1);
      return out;
    }
    return typeof v;
  };

  const describeBody = (text) => {
    if (!text) return '(vazio)';
    try { return JSON.stringify(shape(JSON.parse(text), 0)); }
    catch (e) { return 'texto:' + maskString(String(text).slice(0, 200)); }
  };

  const cleanUrl = (raw) => {
    try {
      const u = new URL(raw, location.href);
      const keys = [...u.searchParams.keys()];
      return u.origin + u.pathname + (keys.length ? '?' + keys.join('&') + '=…' : '');
    } catch (e) { return '(url inválida)'; }
  };

  // as primeiras repetições de cada tipo de mensagem aparecem; depois só 1 a cada 100 (o mercado pode falar o tempo todo)
  const worth = (signature) => {
    const n = (seen.get(signature) || 0) + 1;
    seen.set(signature, n);
    return n <= 3 || n % 100 === 0;
  };

  const origFetch = window.fetch;
  if (origFetch) {
    window.fetch = function (input, init) {
      const url = typeof input === 'string' ? input : (input && input.url) || '';
      const method = ((init && init.method) || (input && input.method) || 'GET').toUpperCase();
      const reqBody = init && typeof init.body === 'string' ? init.body : '';
      const p = origFetch.apply(this, arguments);
      if (!STATIC.test(url)) {
        p.then((res) => {
          const clean = cleanUrl(url);
          const sig = 'fetch ' + method + ' ' + clean;
          if (!worth(sig)) return;
          res.clone().text().then((t) => send('fetch', method + ' ' + clean + ' ' + res.status +
            (reqBody ? ' | enviou ' + describeBody(reqBody) : '') + ' | recebeu ' + describeBody(t))).catch(() => {});
        }).catch(() => {});
      }
      return p;
    };
  }

  const X = window.XMLHttpRequest && window.XMLHttpRequest.prototype;
  if (X) {
    const open = X.open, sendX = X.send;
    X.open = function (method, url) { this.__bi = { method: String(method).toUpperCase(), url: String(url) }; return open.apply(this, arguments); };
    X.send = function (body) {
      const info = this.__bi;
      if (info && !STATIC.test(info.url)) {
        this.addEventListener('loadend', () => {
          const clean = cleanUrl(info.url);
          if (!worth('xhr ' + info.method + ' ' + clean)) return;
          let t = '';
          try { t = typeof this.response === 'string' ? this.response : ''; } catch (e) { /* tipo binário */ }
          send('xhr', info.method + ' ' + clean + ' ' + this.status +
            (typeof body === 'string' && body ? ' | enviou ' + describeBody(body) : '') + ' | recebeu ' + describeBody(t));
        });
      }
      return sendX.apply(this, arguments);
    };
  }

  const WS = window.WebSocket;
  if (WS) {
    window.WebSocket = function (url, protocols) {
      const ws = protocols === undefined ? new WS(url) : new WS(url, protocols);
      const clean = cleanUrl(String(url));
      send('ws-abriu', clean);
      const frame = (dir, data) => {
        if (typeof data !== 'string') return;
        // socket.io: "42[\"evento\",{...}]" — o prefixo numérico fica, o resto vira forma
        const m = /^(\d*)(.*)$/s.exec(data);
        const body = m[2];
        const name = (/^\["([^"]+)"/.exec(body) || /^\s*\{\s*"t"\s*:\s*"([^"]+)"/.exec(body) || [])[1] || '';
        // o mercado e os avisos de resultado nunca são descartados (até 300); o resto segue a regra de repetição
        const key = (/"(?:aba|tipo|ok|erro|codigo)"\s*:\s*"?([^",}]*)/.exec(body) || [])[1] || '';
        if (/^(market|erro|aviso|toast|resultado|msg)/i.test(name)) {
          const n = (seen.get('mk') || 0) + 1;
          seen.set('mk', n);
          if (n > 300) return;
        } else if (!worth('ws ' + dir + ' ' + clean + ' ' + m[1] + ' ' + name + ' ' + key)) return;
        send('ws-' + dir, clean + ' ' + m[1] + ' ' + describeBody(body));
      };
      const origSend = ws.send;
      ws.send = function (data) { frame('enviou', data); return origSend.apply(this, arguments); };
      ws.addEventListener('message', (e) => frame('recebeu', e.data));
      ws.addEventListener('close', (e) => send('ws-fechou', clean + ' ' + e.code));
      return ws;
    };
    window.WebSocket.prototype = WS.prototype;
    Object.assign(window.WebSocket, { CONNECTING: 0, OPEN: 1, CLOSING: 2, CLOSED: 3 });
  }

  send('pronto', location.origin + location.pathname);
})();
