(function () {
  'use strict';

  var KEY_STORAGE = 'hatoclip.key';
  var TIMEOUT_MS = 10000;
  var FLASH_MS = 1500;
  var KEY_MASK = '••••••••••••';

  var MSG = {
    statusConnected: ' に接続中',
    statusOffline: 'PCに接続できません',
    statusNoKey: '接続キーが保存されていません',
    statusBadKey: '接続キーが一致しません',
    statusChecking: '接続を確認しています…',
    setupNoKey: 'PCのQRコードを読み取ってください',
    setupBadKey: '接続キーが一致しません。PCのQRコードを読み直してください',
    unauthorized: '接続キーが一致しません。PCのQRコードを読み直してください',
    offline: 'PCに接続できません。同じWi-Fiに接続しているか、PCでHatoclipが起動しているか確認してください',
    getDisabled: 'PCの設定で取得が許可されていません',
    tooLarge: 'テキストが大きすぎます（1MBまで）',
    tooMany: '認証に失敗した回数が多いため、しばらく待ってから再度お試しください',
    busy: 'PCのクリップボードが使用中です。もう一度お試しください',
    sendFailed: '送信に失敗しました',
    fetchFailed: '取得に失敗しました',
    send: 'PCに送信',
    sending: '送信中…',
    fetching: '取得中…',
    sent: 'PCに届けました',
    empty: 'テキストを入力してください',
    nonText: 'PCのクリップボードはテキストではありません',
    clipEmpty: 'PCのクリップボードは空です',
    copied: 'コピーしました',
    copyFailed: 'コピーできませんでした。テキストを長押ししてコピーしてください',
    pasteDenied: 'クリップボードの読み取りが許可されませんでした',
    keyEmpty: '接続キーを貼り付けてください',
    keyChecking: '確認中…',
    comingSoon: '（準備中）'
  };

  var flashTimers = {};
  var memoryKey = null; // fallback when localStorage is unavailable

  function $(id) { return document.getElementById(id); }

  // ---- key storage ------------------------------------------------------------------------

  function getKey() {
    try {
      var k = localStorage.getItem(KEY_STORAGE);
      if (k) return k;
    } catch (e) { /* storage unavailable */ }
    return memoryKey;
  }

  function saveKey(k) {
    memoryKey = k;
    try { localStorage.setItem(KEY_STORAGE, k); } catch (e) { /* storage unavailable */ }
  }

  // Accepts a bare key or a full connection URL ("http://…/#k=<key>").
  function parseKey(input) {
    var s = (input || '').trim();
    var i = s.indexOf('#');
    if (i >= 0) {
      var k = new URLSearchParams(s.slice(i + 1)).get('k');
      return k ? k.trim() : '';
    }
    return s;
  }

  // Saves a key handed over in the URL fragment (#k=…) and removes the fragment from the
  // address bar and history. Returns true when a key was found.
  function takeKeyFromHash() {
    var hash = location.hash.replace(/^#/, '');
    if (!hash) return false;
    var k = new URLSearchParams(hash).get('k');
    if (k) saveKey(k);
    history.replaceState(null, '', location.pathname + location.search);
    return !!k;
  }

  // ---- UI helpers -------------------------------------------------------------------------

  function pigeonIcon() {
    var ns = 'http://www.w3.org/2000/svg';
    var svg = document.createElementNS(ns, 'svg');
    svg.setAttribute('class', 'msg-icon');
    svg.setAttribute('aria-hidden', 'true');
    var use = document.createElementNS(ns, 'use');
    use.setAttribute('href', '#pigeon');
    svg.appendChild(use);
    return svg;
  }

  // kind: '' | 'ok' | 'error'. withIcon prepends the small flying pigeon.
  function setMsg(el, text, kind, withIcon) {
    clearTimeout(flashTimers[el.id]);
    el.textContent = '';
    if (withIcon) el.appendChild(pigeonIcon());
    if (text) el.appendChild(document.createTextNode(text));
    el.className = 'msg' + (kind ? ' ' + kind : '') + (withIcon ? ' delivered' : '');
  }

  // Shows a message and clears it again after FLASH_MS.
  function flashMsg(el, text, kind) {
    setMsg(el, text, kind);
    flashTimers[el.id] = setTimeout(function () { setMsg(el, ''); }, FLASH_MS);
  }

  function shake(el) {
    el.classList.remove('shake');
    void el.offsetWidth; // restart the animation
    el.classList.add('shake');
  }

  // Grows a textarea with its content up to the CSS max-height; it scrolls beyond that.
  function autoGrow(ta) {
    ta.style.height = 'auto';
    ta.style.height = (ta.scrollHeight + 2) + 'px';
  }

  // Copies text with a temporary textarea + execCommand('copy'): navigator.clipboard is not
  // available on http (non-secure context). Same technique as clipboard.js, which works on iOS.
  function copyText(text) {
    var ta = document.createElement('textarea');
    ta.value = text;
    ta.setAttribute('readonly', '');
    ta.className = 'copy-buffer';
    ta.style.top = (window.pageYOffset || document.documentElement.scrollTop) + 'px';
    document.body.appendChild(ta);
    var ok = false;
    try {
      ta.select();
      ta.setSelectionRange(0, ta.value.length);
      ok = document.execCommand('copy');
    } catch (e) {
      ok = false;
    }
    document.body.removeChild(ta);
    var sel = window.getSelection && window.getSelection();
    if (sel) sel.removeAllRanges();
    return ok;
  }

  // ---- API --------------------------------------------------------------------------------

  // Resolves to { status, body }. status 0 = network error / timeout.
  function api(method, path, payload) {
    var controller = typeof AbortController !== 'undefined' ? new AbortController() : null;
    var timer = controller ? setTimeout(function () { controller.abort(); }, TIMEOUT_MS) : null;
    var headers = { 'X-Hatoclip-Key': getKey() || '' };
    var opts = { method: method, headers: headers, cache: 'no-store' };
    if (controller) opts.signal = controller.signal;
    if (payload !== undefined) {
      headers['Content-Type'] = 'text/plain; charset=utf-8';
      opts.body = payload;
    }
    return fetch(path, opts).then(function (res) {
      return res.json().catch(function () { return null; }).then(function (body) {
        return { status: res.status, body: body };
      });
    }, function () {
      return { status: 0, body: null };
    }).then(function (r) {
      if (timer) clearTimeout(timer);
      return r;
    });
  }

  function isOk(r) { return r.status === 200 && r.body && r.body.ok; }

  // Error text shared by all actions; `failed` is the action-specific text for other codes.
  function errorMessage(status, failed) {
    if (status === 0) return MSG.offline;
    if (status === 401) return MSG.unauthorized;
    if (status === 403) return MSG.getDisabled;
    if (status === 413) return MSG.tooLarge;
    if (status === 429) return MSG.tooMany;
    if (status === 503) return MSG.busy;
    return failed + '（HTTP ' + status + '）';
  }

  // ---- connection state -------------------------------------------------------------------

  function setStatus(text, kind) {
    var el = $('status');
    el.textContent = text;
    el.className = 'status' + (kind ? ' ' + kind : '');
  }

  function showSetup(title) {
    $('setup-title').textContent = title;
    $('setup').hidden = false;
  }

  function setActionsEnabled(enabled) {
    ['fetch-btn', 'copy-btn', 'send-btn', 'paste-send-btn'].forEach(function (id) {
      $(id).disabled = !enabled;
    });
  }

  function refreshKeyField() {
    var shown = $('conn-key-toggle').getAttribute('aria-pressed') === 'true';
    var key = getKey();
    $('conn-key').value = !key ? '' : (shown ? key : KEY_MASK);
  }

  function ping() {
    refreshKeyField();
    if (!getKey()) {
      setStatus(MSG.statusNoKey, 'error');
      showSetup(MSG.setupNoKey);
      setActionsEnabled(false);
      return Promise.resolve();
    }

    setActionsEnabled(true);
    setStatus(MSG.statusChecking);
    return api('GET', '/api/ping').then(function (r) {
      if (isOk(r)) {
        setStatus((r.body.pcName || 'PC') + MSG.statusConnected, 'ok');
        $('setup').hidden = true;
        setShortcutLink('shortcut-send-link', r.body.shortcutSendUrl);
        setShortcutLink('shortcut-receive-link', r.body.shortcutReceiveUrl);
      } else if (r.status === 401) {
        setStatus(MSG.statusBadKey, 'error');
        showSetup(MSG.setupBadKey);
      } else if (r.status === 429) {
        setStatus(MSG.tooMany, 'error');
      } else {
        setStatus(MSG.statusOffline, 'error');
      }
    });
  }

  function saveKeyFromForm(ev) {
    ev.preventDefault();
    var input = $('key-input');
    var msg = $('key-msg');
    var key = parseKey(input.value);
    if (!key) {
      setMsg(msg, MSG.keyEmpty, 'error');
      shake(input);
      return;
    }
    saveKey(key);
    input.value = '';
    input.blur();
    setMsg(msg, MSG.keyChecking);
    ping().then(function () { setMsg(msg, ''); });
  }

  // ---- PC → phone -------------------------------------------------------------------------

  function fetchClipboard() {
    var btn = $('fetch-btn');
    var msg = $('fetch-msg');
    var result = $('fetch-result');
    var out = $('fetched');
    btn.disabled = true;
    setMsg(msg, MSG.fetching);
    api('GET', '/api/clipboard').then(function (r) {
      if (!isOk(r)) {
        setMsg(msg, errorMessage(r.status, MSG.fetchFailed), 'error');
        return;
      }
      var text = r.body.text || '';
      if (r.body.type === 'non-text' || !text) {
        result.hidden = true;
        out.value = '';
        setMsg(msg, r.body.type === 'non-text' ? MSG.nonText : MSG.clipEmpty);
        return;
      }
      out.value = text;
      result.hidden = false;
      autoGrow(out);
      out.scrollTop = 0;
      setMsg(msg, '');
    }).then(function () { btn.disabled = false; });
  }

  function copyFetched() {
    var msg = $('fetch-msg');
    if (copyText($('fetched').value)) flashMsg(msg, MSG.copied, 'ok');
    else setMsg(msg, MSG.copyFailed, 'error');
  }

  // ---- phone → PC -------------------------------------------------------------------------

  function send() {
    var ta = $('send-text');
    var btn = $('send-btn');
    var msg = $('send-msg');
    var text = ta.value;
    if (!text) {
      setMsg(msg, MSG.empty, 'error');
      shake(ta);
      return Promise.resolve();
    }
    btn.disabled = true;
    btn.textContent = MSG.sending;
    setMsg(msg, '');
    return api('POST', '/api/clipboard', text).then(function (r) {
      if (isOk(r)) {
        ta.value = '';
        setMsg(msg, MSG.sent, 'ok', true);
      } else {
        setMsg(msg, errorMessage(r.status, MSG.sendFailed), 'error');
      }
    }).then(function () {
      btn.disabled = false;
      btn.textContent = MSG.send;
    });
  }

  // Only offered in a secure context (HTTPS); hidden on today's http page.
  function canReadClipboard() {
    return !!(window.isSecureContext && navigator.clipboard && navigator.clipboard.readText);
  }

  function pasteAndSend() {
    navigator.clipboard.readText().then(function (text) {
      $('send-text').value = text;
      return send();
    }, function () {
      setMsg($('send-msg'), MSG.pasteDenied, 'error');
    });
  }

  // ---- shortcut section -------------------------------------------------------------------

  // iOS Shortcut iCloud links come from the PC's config.json via /api/ping (see docs/shortcut.md)
  // and are re-evaluated on every successful ping. An empty URL is shown as「（準備中）」and
  // cannot be tapped.
  function setShortcutLink(id, url) {
    var a = $(id);
    var label = a.getAttribute('data-label');
    if (typeof url === 'string' && /^https?:\/\//i.test(url)) {
      a.href = url;
      a.target = '_blank';
      a.rel = 'noopener';
      a.removeAttribute('aria-disabled');
      a.classList.remove('is-disabled');
      a.textContent = label;
    } else {
      a.removeAttribute('href');
      a.removeAttribute('target');
      a.setAttribute('aria-disabled', 'true');
      a.classList.add('is-disabled');
      a.textContent = label + MSG.comingSoon;
    }
  }

  function toggleKeyVisibility() {
    var btn = $('conn-key-toggle');
    var shown = btn.getAttribute('aria-pressed') !== 'true';
    btn.setAttribute('aria-pressed', shown ? 'true' : 'false');
    btn.textContent = shown ? '隠す' : '表示';
    refreshKeyField();
  }

  function copyDetail(value) {
    var msg = $('details-msg');
    if (!value) return;
    if (copyText(value)) flashMsg(msg, MSG.copied, 'ok');
    else setMsg(msg, MSG.copyFailed, 'error');
  }

  // ---- init -------------------------------------------------------------------------------

  function clearShake() { this.classList.remove('shake'); }

  function init() {
    takeKeyFromHash();

    $('reconnect-btn').addEventListener('click', ping);
    $('key-form').addEventListener('submit', saveKeyFromForm);
    $('fetch-btn').addEventListener('click', fetchClipboard);
    $('copy-btn').addEventListener('click', copyFetched);
    $('send-btn').addEventListener('click', send);
    $('send-text').addEventListener('animationend', clearShake);
    $('key-input').addEventListener('animationend', clearShake);

    if (canReadClipboard()) {
      $('paste-send-btn').hidden = false;
      $('paste-send-btn').addEventListener('click', pasteAndSend);
    }

    $('conn-url').value = location.origin;
    $('conn-url-copy').addEventListener('click', function () { copyDetail(location.origin); });
    $('conn-key-toggle').addEventListener('click', toggleKeyVisibility);
    $('conn-key-copy').addEventListener('click', function () { copyDetail(getKey()); });

    // A new key scanned while the page is already open (same tab, new #k=…): overwrite and reconnect.
    window.addEventListener('hashchange', function () { if (takeKeyFromHash()) ping(); });

    ping();
  }

  init();
})();
