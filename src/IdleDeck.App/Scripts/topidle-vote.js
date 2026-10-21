// Roda só no topidle.com, dentro da janela "Votar no TopIdle".
// Avisa o app UMA vez quando a página passa a mostrar "Voto validado e contabilizado".
// Nunca clica em nada: quem vota é o usuário.
(() => {
  const host = location.hostname.toLowerCase();
  if (host !== 'topidle.com' && !host.endsWith('.topidle.com')) return;

  const phrase = 'voto validado e contabilizado';
  const flag = 'bookidle:topidle:sem-voto';
  let baselineKnown = false; // só vale a transição "não tinha → apareceu": texto já presente no carregamento é ignorado
  let present = false;
  let sent = false;
  let timer = 0;

  // espaços não separáveis e quebras de linha não podem impedir a comparação
  const visible = () => ((document.body && document.body.innerText) || '')
    .toLowerCase().replace(/\s+/g, ' ').includes(phrase);

  // a página pode recarregar inteira ao confirmar o voto: o "não tinha" visto no documento anterior vale para este
  const hadNone = () => { try { return sessionStorage.getItem(flag) === '1'; } catch (e) { return false; } };
  const markNone = () => { try { sessionStorage.setItem(flag, '1'); } catch (e) { /* sem armazenamento */ } };

  const notify = () => {
    sent = true;
    try { sessionStorage.removeItem(flag); } catch (e) { /* sem armazenamento */ }
    try { window.chrome.webview.postMessage('topidle:voted'); } catch (e) { /* sem canal com o app */ }
  };

  const check = () => {
    timer = 0;
    if (sent) return;
    const now = visible();
    if (!baselineKnown) {
      baselineKnown = true;
      present = now;
      if (!now) markNone();
      else if (hadNone()) notify();
      return;
    }
    if (now && !present) notify();
    else if (!now) markNone();
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
