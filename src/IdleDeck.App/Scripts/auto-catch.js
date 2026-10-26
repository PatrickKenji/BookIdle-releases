// Captura automática (para quem não tem o VIP): a cada segundo clica nos botões dos pokémons caídos (button.caido).
// Fica sempre carregado no PokeIdle, mas só clica com a chave window.__bookidleAutoCatch ligada; o app liga e desliga por conta.
(() => {
  if (window.__bookidleAutoCatchLoop) return;
  const host = location.hostname.toLowerCase();
  if (host !== 'pokeidle.io' && !host.endsWith('.pokeidle.io')) return;
  window.__bookidleAutoCatchLoop = true;

  setInterval(() => {
    if (window.__bookidleAutoCatch !== true || !window.document) return;
    try {
      window.document.querySelectorAll('button.caido').forEach((botao) => {
        if (!botao.disabled) botao.click();
      });
    } catch (e) { /* nunca atrapalha o jogo */ }
  }, 1000);
})();
