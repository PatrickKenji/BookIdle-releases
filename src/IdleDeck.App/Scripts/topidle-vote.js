// Roda só no topidle.com, dentro da janela "Votar no TopIdle".
// Avisa o app UMA vez quando a página passa a mostrar "Voto validado e contabilizado".
// Nunca clica em nada: quem vota é o usuário.
(() => {
  const host = location.hostname.toLowerCase();
  if (host !== 'topidle.com' && !host.endsWith('.topidle.com')) return;

  const phrase = 'voto validado e contabilizado';
  let baselineKnown = false; // só vale a transição "não tinha → apareceu": texto já presente no carregamento é ignorado
  let present = false;
  let sent = false;
  let timer = 0;

  const visible = () => ((document.body && document.body.innerText) || '').toLowerCase().includes(phrase);

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
    new MutationObserver(schedule).observe(document.documentElement, { childList: true, subtree: true, characterData: true });
    setTimeout(check, 1500); // primeira leitura: define o que já estava na tela
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})();
