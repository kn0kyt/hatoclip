(function () {
  'use strict';

  var KEY_STORAGE = 'hatoclip.key';
  var TIMEOUT_MS = 10000;

  var MSG = {
    noKey: 'PCのQRコードを読み取ってください',
    unauthorized: '接続キーが一致しません。PCのQRコードを読み直してください',
    offline: 'PCに接続できません。同じWi-Fiに接続しているか確認してください',
    getDisabled: 'PCの設定で取得が許可されていません',
    tooLarge: 'テキストが大きすぎます（最大 1MB）',
    tooMany: '認証の失敗が続いたため、しばらく接続できません。時間をおいて再度お試しください',
    busy: 'PCのクリップボードが他のアプリで使用中です。もう一度お試しください',
    unknown: 'エラーが発生しました',
    sent: 'PCに届けました',
    empty: '送信するテキストを入力してください',
    fetched: 'PCのクリップボードを取得しました',
    nonText: 'PCのクリップボードにテキスト以外のデータが入っています',
    clipEmpty: 'PCのクリップボードは空です',
    copied: 'コピーしました',
    copyFailed: 'コピーできませんでした。テキストを長押ししてコピーしてください'
  };

  function $(id) { return document.getElementById(id); }

  function readKeyFromHash() {
    var hash = location.hash.replace(/^#/, '');
    if (!hash) return;
    var params = new URLSearchParams(hash);
    var k = params.get('k');
    if (k) {
      try { localStorage.setItem(KEY_STORAGE, k); } catch (e) { /* storage unavailable */ }
      window.__hatoclipKey = k;
    }
    // Remove the fragment (and the key) from the address bar / history.
    history.replaceState(null, '', location.pathname + location.search);
  }

  function getKey() {
    try {
      var k = localStorage.getItem(KEY_STORAGE);
      if (k) return k;
    } catch (e) { /* storage unavailable */ }
    return window.__hatoclipKey || null;
  }

  function setMsg(el, text, kind) {
    el.textContent = text || '';
    el.className = 'msg' + (kind ? ' ' + kind : '');
  }

  function errorMessage(status, body) {
    if (status === 0) return MSG.offline;
    if (status === 401) return MSG.unauthorized;
    if (status === 403) return MSG.getDisabled;
    if (status === 413) return MSG.tooLarge;
    if (status === 429) return MSG.tooMany;
    if (status === 503) return MSG.busy;
    return MSG.unknown + (body && body.error ? '（' + body.error + '）' : '（' + status + '）');
  }

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

  function ping() {
    var status = $('status');
    return api('GET', '/api/ping').then(function (r) {
      if (r.status === 200 && r.body && r.body.ok) {
        status.textContent = '接続先: ' + r.body.pcName;
        status.className = 'status';
      } else {
        status.textContent = errorMessage(r.status, r.body);
        status.className = 'status error';
      }
    });
  }

  function fetchClipboard() {
    var btn = $('fetch-btn');
    var out = $('fetched');
    var msg = $('fetch-msg');
    btn.disabled = true;
    setMsg(msg, '取得しています…');
    api('GET', '/api/clipboard').then(function (r) {
      if (r.status === 200 && r.body && r.body.ok) {
        out.value = r.body.text || '';
        $('copy-btn').disabled = !out.value;
        if (r.body.type === 'non-text') setMsg(msg, MSG.nonText, 'error');
        else if (!out.value) setMsg(msg, MSG.clipEmpty);
        else setMsg(msg, MSG.fetched, 'ok');
      } else {
        setMsg(msg, errorMessage(r.status, r.body), 'error');
      }
    }).then(function () { btn.disabled = false; });
  }

  function copyFetched() {
    var ta = $('fetched');
    var msg = $('fetch-msg');
    var ok = false;
    // http (non-secure context): navigator.clipboard is unavailable, use execCommand.
    ta.removeAttribute('readonly');
    try {
      ta.focus();
      ta.select();
      ta.setSelectionRange(0, ta.value.length);
      ok = document.execCommand('copy');
    } catch (e) {
      ok = false;
    }
    ta.setAttribute('readonly', '');
    ta.blur();
    setMsg(msg, ok ? MSG.copied : MSG.copyFailed, ok ? 'ok' : 'error');
  }

  function send() {
    var ta = $('send-text');
    var btn = $('send-btn');
    var msg = $('send-msg');
    var text = ta.value;
    if (!text) {
      setMsg(msg, MSG.empty, 'error');
      return;
    }
    btn.disabled = true;
    setMsg(msg, '送信しています…');
    api('POST', '/api/clipboard', text).then(function (r) {
      if (r.status === 200 && r.body && r.body.ok) {
        ta.value = '';
        setMsg(msg, MSG.sent, 'ok');
      } else {
        setMsg(msg, errorMessage(r.status, r.body), 'error');
      }
    }).then(function () { btn.disabled = false; });
  }

  function init() {
    readKeyFromHash();

    $('fetch-btn').addEventListener('click', fetchClipboard);
    $('copy-btn').addEventListener('click', copyFetched);
    $('send-btn').addEventListener('click', send);

    if (!getKey()) {
      $('no-key').hidden = false;
      $('status').textContent = MSG.noKey;
      $('status').className = 'status error';
      $('fetch-btn').disabled = true;
      $('send-btn').disabled = true;
      return;
    }

    ping();
  }

  init();
})();
