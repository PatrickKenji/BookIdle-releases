// Injetado em toda página das contas (e nos popups delas).
// No site do jogo: avisa "form" quando há tela de login visível (campo de senha ou botão do Google;
// repetido a cada 20 s enquanto continuar visível) e "ok" quando ela sumiu por 60 s.
// No Google (accounts.google.com): avisa "google-chooser" | "google-password" | "google-identifier" | "google-blocked".
// Expõe para o app: __idledeckFillLogin(usuario, senha, enviar), __idledeckClickGoogle(), __idledeckPickGoogleAccount(email).
// Nunca preenche nada em páginas do Google.
(() => {
  if (window.top !== window || window.__idledeckLogin) return;
  window.__idledeckLogin = true;

  const REPOST_MS = 20000;
  const OK_AFTER_MS = 60000;

  const visible = (el) => !!(el && (el.offsetWidth || el.offsetHeight || el.getClientRects().length));
  const passwordField = () => [...document.querySelectorAll('input[type=password]')].find(visible);
  const post = (state) => {
    try { window.chrome.webview.postMessage(JSON.stringify({ app: 'login', state })); } catch { }
  };
  const onGoogle = location.hostname === 'accounts.google.com';

  // Botão/link "Entrar com Google" do jogo: texto curto de login + Google (ex.: "Login com Google"),
  // ou link direto para accounts.google.com. Ignora Google Play e textos longos (cards, rodapés).
  const LOGIN_WITH_GOOGLE = /(entrar|login|logar|acessar|sign in|continuar|continue).{0,20}google|google.{0,20}(login|sign in|entrar)/i;
  const googleButton = () => [...document.querySelectorAll('button, a, [role=button]')].filter(visible).find((el) => {
    const href = el.getAttribute('href') || '';
    if (/play\.google\.com/i.test(href)) return false;
    if (/accounts\.google\.com/i.test(href)) return true;
    const label = [el.textContent, el.title, el.getAttribute('aria-label'),
      ...[...el.querySelectorAll('img')].map((i) => i.alt)].join(' ').replace(/\s+/g, ' ').trim();
    return label.length <= 60 && LOGIN_WITH_GOOGLE.test(label);
  });
  const loginVisible = () => !!(passwordField() || googleButton());

  // Estado da página do Google.
  const googleState = () => {
    const text = document.body ? document.body.innerText : '';
    if (/não é seguro|may not be secure|couldn.t sign you in|não foi possível fazer login/i.test(text)) return 'google-blocked';
    if (passwordField()) return 'google-password'; // antes do seletor: a tela de senha também mostra a conta
    if ([...document.querySelectorAll('[data-identifier], [data-email]')].some(visible)) return 'google-chooser';
    if ([...document.querySelectorAll('input[type=email], #identifierId')].some(visible)) return 'google-identifier';
    return null;
  };
  let lastGoogleState = null;
  let lastGooglePost = 0;
  const checkGoogle = () => {
    const state = googleState();
    if (!state) return;
    if (state !== lastGoogleState || Date.now() - lastGooglePost >= REPOST_MS) {
      lastGoogleState = state;
      lastGooglePost = Date.now();
      post(state);
    }
  };

  let lastFormPost = 0;
  let formSeen = false;
  let okTimer = null;
  let debounce = null;

  const check = () => {
    if (onGoogle) return checkGoogle();
    if (loginVisible()) {
      formSeen = true;
      clearTimeout(okTimer);
      okTimer = null;
      if (Date.now() - lastFormPost >= REPOST_MS) {
        lastFormPost = Date.now();
        post('form');
      }
    } else if (formSeen && !okTimer) {
      okTimer = setTimeout(() => {
        okTimer = null;
        if (!loginVisible()) {
          formSeen = false;
          post('ok');
        }
      }, OK_AFTER_MS);
    }
  };

  const schedule = () => {
    clearTimeout(debounce);
    debounce = setTimeout(check, 1000);
  };

  const start = () => {
    new MutationObserver(schedule).observe(document.documentElement, {
      childList: true, subtree: true, attributes: true, attributeFilter: ['style', 'class', 'hidden'],
    });
    setInterval(check, REPOST_MS);
    schedule();
  };

  if (document.documentElement) start();
  else document.addEventListener('DOMContentLoaded', start);

  // Preenche usando o setter nativo + eventos, para funcionar com React/Vue.
  const setValue = (el, value) => {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
    el.focus();
    setter.call(el, value);
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
  };

  window.__idledeckFillLogin = (user, pass, submit) => {
    if (onGoogle) return false;
    const pw = passwordField();
    if (!pw) return false;
    const scope = pw.form || pw.closest('form, [role=dialog], .modal') || document;
    const candidates = [...scope.querySelectorAll('input')]
      .filter((i) => visible(i) && ['text', 'email', 'tel', ''].includes(i.type));
    const before = candidates.filter((i) => i.compareDocumentPosition(pw) & Node.DOCUMENT_POSITION_FOLLOWING);
    const userInput = before.pop() || candidates[0];

    if (userInput) setValue(userInput, user);
    setValue(pw, pass);
    if (!submit) return true;

    setTimeout(() => {
      const buttons = [...scope.querySelectorAll('button, input[type=submit]')].filter(visible);
      const button = buttons.find((b) => b.type === 'submit')
        || buttons.find((b) => /entrar|login|log in|sign in|acessar/i.test(b.textContent || b.value || ''));
      if (button) button.click();
      else if (pw.form) pw.form.requestSubmit();
      else pw.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', bubbles: true }));
    }, 400);
    return true;
  };

  window.__idledeckClickGoogle = () => {
    if (onGoogle) return false;
    const button = googleButton();
    if (!button) return false;
    button.click();
    return true;
  };

  // Só clica na conta certa do seletor do Google; nunca digita nada.
  window.__idledeckPickGoogleAccount = (email) => {
    if (!onGoogle || !email) return false;
    const wanted = String(email).trim().toLowerCase();
    const byData = [...document.querySelectorAll('[data-identifier], [data-email]')].filter(visible).find((el) =>
      [el.getAttribute('data-identifier'), el.getAttribute('data-email')].some((v) => (v || '').trim().toLowerCase() === wanted));
    const byText = byData || [...document.querySelectorAll('li, [role=link], [role=button], div')].filter(visible)
      .filter((el) => (el.innerText || '').toLowerCase().includes(wanted))
      .sort((a, b) => (a.innerText || '').length - (b.innerText || '').length)[0];
    if (!byText) return false;
    (byText.closest('[role=link], [role=button], li, a, button') || byText).click();
    return true;
  };
})();
