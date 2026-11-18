// Roda só no idlehub.io, dentro da janela de voto.
// Avisa o app UMA vez quando a caixa de voto vira o quadro "Voto registrado ✓" (div .voto-feito, que o site só cria
// depois que o servidor aceitou o voto). Olha a classe, não o texto: o site muda de idioma.
// Nunca clica em nada: quem vota é o usuário.
(() => {
  const host = location.hostname.toLowerCase();
  if (host !== 'idlehub.io' && !host.endsWith('.idlehub.io')) return;

  let baselineKnown = false; // só vale a transição "não tinha → apareceu": quadro já presente no carregamento é ignorado
  let present = false;
  let sent = false;
  let timer = 0;

  const visible = () => !!document.querySelector('.voto-feito');

  const check = () => {
    timer = 0;
    if (sent) return;
    const now = visible();
    if (!baselineKnown) {
      baselineKnown = true;
      present = now;
      return;
    }
    if (now && !present) {
      sent = true;
      try { window.chrome.webview.postMessage('topidle:voted'); } catch (e) { /* sem canal com o app */ }
    }
    present = now;
  };

  const schedule = () => { if (!timer && !sent) timer = setTimeout(check, 400); };

  const start = () => {
    new MutationObserver(schedule).observe(document.documentElement, { childList: true, subtree: true });
    setTimeout(check, 1500); // primeira leitura: define o que já estava na tela
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})();
