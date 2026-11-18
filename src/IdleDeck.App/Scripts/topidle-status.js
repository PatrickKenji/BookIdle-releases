// Lê, da página do jogo, o status do voto da conta (a resposta do POST /idlehub/me — antes /topidle/me — que o próprio
// jogo já faz) e avisa o app só com DOIS números: quando foi o último voto e quando libera de novo.
// O /idlehub/me não diz o último voto, só quanto falta (esperaMs): vira "libera em agora + esperaMs".
// Nunca lê nem repassa token, nome ou link; não faz pedido nenhum, só observa o que o jogo já pediu.
(() => {
  if (window.__bookidleTopIdleStatus) return;
  const host = location.hostname.toLowerCase();
  if (host !== 'pokeidle.io' && !host.endsWith('.pokeidle.io')) return;
  const originalFetch = window.fetch;
  if (typeof originalFetch !== 'function') return;
  window.__bookidleTopIdleStatus = true;

  const relay = (data) => {
    const last = Number(data && data.ultimoVotoEm);
    const release = Number(data && data.liberaEm);
    const wait = Number(data && data.esperaMs);
    const body = {};
    if (Number.isFinite(last) && last > 0) body.ultimoVotoEm = last;
    if (Number.isFinite(release) && release > 0) body.liberaEm = release;
    else if (data && data.ativo !== false && Number.isFinite(wait)) body.liberaEm = Date.now() + Math.max(0, wait);
    if (!Object.keys(body).length) return;
    try { window.chrome.webview.postMessage('topidle:status:' + JSON.stringify(body)); } catch (e) { /* fora do app */ }
  };

  window.fetch = function (input) {
    const promise = originalFetch.apply(this, arguments);
    try {
      const url = typeof input === 'string' ? input : (input && input.url) || '';
      if (url.indexOf('/idlehub/me') >= 0 || url.indexOf('/topidle/me') >= 0) {
        promise.then((res) => res.clone().json().then(relay, () => {}), () => {});
      }
    } catch (e) { /* nunca atrapalha o jogo */ }
    return promise;
  };
})();
