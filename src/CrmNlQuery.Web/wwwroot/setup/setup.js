(function () {
  'use strict';

  var EN = {
    title: 'First-time setup',
    step: 'Step {n} of {total}',
    codeTitle: 'Enter the setup code',
    codeHelp: 'The code is printed in the service log. In Docker run: docker compose logs app',
    codeLabel: 'Setup code',
    unlock: 'Continue',
    dbDown: 'The database cannot be reached. Check History:ConnectionString and that PostgreSQL is running, then reload this page.',
    crmTitle: 'Connect to Dynamics CRM',
    crmUrl: 'CRM address (for example http://crm-server/org)',
    crmDomain: 'Windows domain',
    crmUser: 'Service account user name',
    crmPassword: 'Service account password',
    crmHelp: 'Use the least-privileged service account prepared for this tool. The password is stored encrypted.',
    modelTitle: 'Choose the language model',
    provider: 'Model provider',
    modelUrl: 'Model server address',
    modelName: 'Model name',
    modelKey: 'API key (if the server needs one)',
    cloudKey: 'Anthropic API key',
    confirmNetwork: 'This model server is not on this machine. I confirm it is inside our own network, because question text is sent to it.',
    confirmCloud: 'I understand the cloud model sends question text outside the company and is meant for lab or trial use only.',
    adminTitle: 'Administrator and address',
    adminLabel: 'First administrator (DOMAIN\\user)',
    serviceUrl: 'Address users open this service on',
    serviceHelp: 'Use the address the CRM server can reach, for example http://this-machine:5080.',
    test: 'Test connection',
    testing: 'Testing...',
    next: 'Next',
    back: 'Back',
    finish: 'Finish setup',
    saving: 'Saving...',
    doneTitle: 'Setup is complete',
    doneText: 'The service is restarting with your settings. This page opens the tool when it is ready.',
    ok: 'OK',
    warn: 'Note',
    fail: 'Problem',
    'err.invalid_code': 'That code is not correct.',
    'err.locked': 'Too many wrong codes. Wait five minutes, or restart the service for a new code.',
    'err.unauthorized': 'The session expired. Enter the code again.',
    'err.invalid_url': 'The address is not valid. Use http:// or https:// without a user name or query.',
    'err.invalid_value': 'A value is missing or not valid: {field}',
    'err.confirmation_required': 'Please tick the confirmation box.',
    'err.database_unavailable': 'The database cannot be reached.',
    'err.save_failed': 'The settings could not be saved. See the service log.',
    'err.not_available': 'Setup is already finished.',
    'err.network': 'The service did not answer.',
    'check.crm.http': 'The CRM address uses http, so the password travels unencrypted. Use https outside a lab.',
    'check.crm.signin': 'Sign in to the CRM',
    'check.crm.schema': 'Read the CRM schema',
    'check.crm.users': 'List CRM users',
    'check.crm.impersonate': 'Act on behalf of another user',
    'check.crm.groups': "Read users' security roles and teams",
    'check.crm.tasks': 'Read sign-in proof tasks',
    'check.crm.admin': 'This account holds the System Administrator role. Give it only the permissions the tool needs.',
    'check.model.reach': 'Reach the model server',
    'check.model.list': 'Read the model list',
    'check.model.present': 'The chosen model is installed',
    'detail.unauthorized': 'wrong user name, password or key',
    'detail.forbidden': 'not permitted',
    'detail.not_found': 'not found',
    'detail.unreachable': 'cannot be reached',
    'detail.timeout': 'no answer in time',
    'detail.error': 'the server returned an error',
    'detail.unexpected_answer': 'unexpected answer',
    'detail.no_other_user': 'no second user to test with',
    'detail.not_listed': 'the model is not on the server'
  };

  var TR = {
    title: 'İlk kurulum',
    step: 'Adım {n} / {total}',
    codeTitle: 'Kurulum kodunu girin',
    codeHelp: 'Kod servis günlüğüne yazılır. Docker için: docker compose logs app',
    codeLabel: 'Kurulum kodu',
    unlock: 'Devam',
    dbDown: 'Veritabanına ulaşılamıyor. History:ConnectionString ayarını ve PostgreSQL\'in çalıştığını kontrol edin, sonra sayfayı yenileyin.',
    crmTitle: 'Dynamics CRM bağlantısı',
    crmUrl: 'CRM adresi (örnek http://crm-sunucu/org)',
    crmDomain: 'Windows etki alanı',
    crmUser: 'Servis hesabı kullanıcı adı',
    crmPassword: 'Servis hesabı parolası',
    crmHelp: 'Bu araç için hazırlanmış en az yetkili servis hesabını kullanın. Parola şifreli saklanır.',
    modelTitle: 'Dil modelini seçin',
    provider: 'Model sağlayıcı',
    modelUrl: 'Model sunucusu adresi',
    modelName: 'Model adı',
    modelKey: 'API anahtarı (sunucu istiyorsa)',
    cloudKey: 'Anthropic API anahtarı',
    confirmNetwork: 'Bu model sunucusu bu makinede değil. Soru metni ona gönderildiği için kendi ağımızın içinde olduğunu onaylıyorum.',
    confirmCloud: 'Bulut modelinin soru metnini şirket dışına gönderdiğini ve yalnız lab veya deneme için olduğunu anlıyorum.',
    adminTitle: 'Yönetici ve adres',
    adminLabel: 'İlk yönetici (ALAN\\kullanıcı)',
    serviceUrl: 'Kullanıcıların bu servisi açtığı adres',
    serviceHelp: 'CRM sunucusunun ulaşabildiği adresi yazın, örnek http://bu-makine:5080.',
    test: 'Bağlantıyı dene',
    testing: 'Deneniyor...',
    next: 'İleri',
    back: 'Geri',
    finish: 'Kurulumu bitir',
    saving: 'Kaydediliyor...',
    doneTitle: 'Kurulum tamamlandı',
    doneText: 'Servis ayarlarınızla yeniden başlıyor. Hazır olunca bu sayfa aracı açar.',
    ok: 'Tamam',
    warn: 'Not',
    fail: 'Sorun',
    'err.invalid_code': 'Kod doğru değil.',
    'err.locked': 'Çok fazla yanlış kod. Beş dakika bekleyin ya da yeni kod için servisi yeniden başlatın.',
    'err.unauthorized': 'Oturum süresi doldu. Kodu yeniden girin.',
    'err.invalid_url': 'Adres geçerli değil. Kullanıcı adı ya da sorgu olmadan http:// veya https:// kullanın.',
    'err.invalid_value': 'Bir değer eksik ya da geçersiz: {field}',
    'err.confirmation_required': 'Lütfen onay kutusunu işaretleyin.',
    'err.database_unavailable': 'Veritabanına ulaşılamıyor.',
    'err.save_failed': 'Ayarlar kaydedilemedi. Servis günlüğüne bakın.',
    'err.not_available': 'Kurulum zaten bitti.',
    'err.network': 'Servis cevap vermedi.',
    'check.crm.http': 'CRM adresi http kullanıyor, parola şifresiz gider. Lab dışında https kullanın.',
    'check.crm.signin': 'CRM\'e giriş',
    'check.crm.schema': 'CRM şemasını okuma',
    'check.crm.users': 'CRM kullanıcılarını listeleme',
    'check.crm.impersonate': 'Başka kullanıcı adına işlem yapma',
    'check.crm.groups': 'Kullanıcıların rol ve takımlarını okuma',
    'check.crm.tasks': 'Giriş kanıtı görevlerini okuma',
    'check.crm.admin': 'Bu hesap System Administrator rolünde. Ona yalnız aracın ihtiyaç duyduğu yetkileri verin.',
    'check.model.reach': 'Model sunucusuna ulaşma',
    'check.model.list': 'Model listesini okuma',
    'check.model.present': 'Seçilen model kurulu',
    'detail.unauthorized': 'kullanıcı adı, parola ya da anahtar yanlış',
    'detail.forbidden': 'izin yok',
    'detail.not_found': 'bulunamadı',
    'detail.unreachable': 'ulaşılamıyor',
    'detail.timeout': 'zamanında cevap yok',
    'detail.error': 'sunucu hata döndürdü',
    'detail.unexpected_answer': 'beklenmeyen cevap',
    'detail.no_other_user': 'denemek için ikinci bir kullanıcı yok',
    'detail.not_listed': 'model sunucuda yok'
  };

  var lang = (navigator.language || 'en').toLowerCase().indexOf('tr') === 0 ? 'tr' : 'en';
  document.documentElement.lang = lang;
  var T = lang === 'tr' ? TR : EN;

  function t(key, vars) {
    var text = T[key] || EN[key] || key;
    if (vars) {
      Object.keys(vars).forEach(function (k) { text = text.split('{' + k + '}').join(vars[k]); });
    }
    return text;
  }

  var app = document.getElementById('app');
  var session = null;
  var state = {
    crm: { baseUrl: '', domain: '', username: '', password: '' },
    model: { provider: 'Ollama', baseUrl: 'http://host.docker.internal:11434', model: '', apiKey: '' },
    confirmLocalNetwork: false,
    confirmCloudLab: false,
    adminAccount: '',
    serviceUrl: location.origin
  };
  var crmOk = false;
  var modelOk = false;

  document.title = t('title');

  function el(tag, attrs, children) {
    var node = document.createElement(tag);
    Object.keys(attrs || {}).forEach(function (k) {
      if (k === 'text') { node.textContent = attrs[k]; } else if (k === 'on') { Object.keys(attrs.on).forEach(function (e) { node.addEventListener(e, attrs.on[e]); }); } else { node.setAttribute(k, attrs[k]); }
    });
    (children || []).forEach(function (c) { if (c) { node.appendChild(c); } });
    return node;
  }

  function api(path, body, method) {
    var headers = { 'Content-Type': 'application/json' };
    if (session) { headers['X-Setup-Session'] = session; }
    return fetch(path, { method: method || 'POST', headers: headers, body: body === undefined ? undefined : JSON.stringify(body) })
      .then(function (r) { return r.json().catch(function () { return {}; }).then(function (j) { return { status: r.status, body: j }; }); })
      .catch(function () { return { status: 0, body: { error: 'network' } }; });
  }

  function errorText(body) {
    return t('err.' + (body.error || 'network'), { field: body.field || '' });
  }

  function field(id, labelKey, type, value, onInput, autocomplete) {
    var input = el('input', { id: id, type: type, value: value || '', autocomplete: autocomplete || 'off', spellcheck: 'false' });
    input.addEventListener('input', function () { onInput(input.value); });
    return [el('label', { for: id, text: t(labelKey) }), input];
  }

  function header(stepNo, titleKey) {
    var parts = [el('h1', { text: t(titleKey) })];
    if (stepNo) { parts.push(el('p', { class: 'steps', text: t('step', { n: stepNo, total: 3 }) })); }
    return parts;
  }

  function render(nodes) {
    app.textContent = '';
    nodes.forEach(function (n) { if (n) { app.appendChild(n); } });
    var first = app.querySelector('input,select');
    if (first) { first.focus(); }
  }

  function message(kind, text) {
    return el('div', { class: 'msg ' + kind, role: kind === 'bad' ? 'alert' : 'status', text: text });
  }

  function results(checks) {
    var list = el('ul', { class: 'results' });
    checks.forEach(function (c) {
      var status = c.status;
      var cls = status === 'Ok' || status === 0 ? 'ok' : (status === 'Warn' || status === 1 ? 'warn' : 'fail');
      var label = cls === 'ok' ? t('ok') : (cls === 'warn' ? t('warn') : t('fail'));
      var text = t('check.' + c.id);
      if (c.detail) { text += ' - ' + t('detail.' + c.detail); }
      list.appendChild(el('li', {}, [el('span', { class: 'st ' + cls, text: label }), el('span', { text: ' ' + text })]));
    });
    return list;
  }

  function hasFail(checks) {
    return checks.some(function (c) { return !(c.status === 'Ok' || c.status === 0 || c.status === 'Warn' || c.status === 1); });
  }

  function showCode(stateBody, note) {
    var input = el('input', { id: 'code', type: 'text', autocomplete: 'off', spellcheck: 'false', placeholder: 'XXXX-XXXX-XXXX' });
    var box = el('div');
    var button = el('button', { type: 'button', text: t('unlock') });
    function go() {
      button.disabled = true;
      api('/api/setup/unlock', { code: input.value }).then(function (r) {
        button.disabled = false;
        if (r.status === 200 && r.body.session) { session = r.body.session; showCrm(); return; }
        box.textContent = '';
        box.appendChild(message('bad', errorText(r.body)));
      });
    }
    button.addEventListener('click', go);
    input.addEventListener('keydown', function (e) { if (e.key === 'Enter') { go(); } });
    render(header(0, 'codeTitle').concat([
      el('p', { text: t('codeHelp') }),
      stateBody && !stateBody.databaseAvailable ? message('bad', t('dbDown')) : null,
      stateBody && stateBody.locked ? message('warn', t('err.locked')) : null,
      note ? message('bad', note) : null,
      el('label', { for: 'code', text: t('codeLabel') }), input, box,
      el('div', { class: 'row' }, [button])
    ]));
  }

  function showCrm(resultBox) {
    var box = el('div');
    if (resultBox) { box.appendChild(resultBox); }
    var test = el('button', { type: 'button', class: 'secondary', text: t('test') });
    var next = el('button', { type: 'button', text: t('next') });
    next.disabled = !crmOk;
    function changed() { crmOk = false; next.disabled = true; }
    test.addEventListener('click', function () {
      test.disabled = true;
      test.textContent = t('testing');
      api('/api/setup/test-crm', state.crm).then(function (r) {
        test.disabled = false;
        test.textContent = t('test');
        box.textContent = '';
        if (r.status !== 200) {
          if (r.status === 401) { session = null; showCode(null, t('err.unauthorized')); return; }
          box.appendChild(message('bad', errorText(r.body)));
          return;
        }
        crmOk = !hasFail(r.body.checks);
        next.disabled = !crmOk;
        box.appendChild(results(r.body.checks));
      });
    });
    next.addEventListener('click', function () { showModel(); });
    function on(k) { return function (v) { state.crm[k] = v; changed(); }; }
    render(header(1, 'crmTitle').concat([
      el('p', { class: 'hint', text: t('crmHelp') })
    ], field('crm-url', 'crmUrl', 'text', state.crm.baseUrl, on('baseUrl')),
      field('crm-domain', 'crmDomain', 'text', state.crm.domain, on('domain')),
      field('crm-user', 'crmUser', 'text', state.crm.username, on('username')),
      field('crm-pass', 'crmPassword', 'password', state.crm.password, on('password'), 'new-password'),
      [box, el('div', { class: 'row' }, [test, next])]));
  }

  function showModel(resultBox) {
    var box = el('div');
    if (resultBox) { box.appendChild(resultBox); }
    var select = el('select', { id: 'provider' });
    ['Ollama', 'Vllm', 'Anthropic'].forEach(function (p) {
      var o = el('option', { value: p, text: p === 'Vllm' ? 'vLLM' : p });
      if (p === state.model.provider) { o.selected = true; }
      select.appendChild(o);
    });
    select.addEventListener('change', function () {
      state.model.provider = select.value;
      state.model.baseUrl = select.value === 'Vllm' ? 'http://vllm:8000' : (select.value === 'Ollama' ? 'http://host.docker.internal:11434' : '');
      modelOk = false;
      showModel();
    });
    var cloud = state.model.provider === 'Anthropic';
    var test = el('button', { type: 'button', class: 'secondary', text: t('test') });
    var next = el('button', { type: 'button', text: t('next') });
    var back = el('button', { type: 'button', class: 'secondary', text: t('back') });
    next.disabled = !modelOk;
    function changed() { modelOk = false; next.disabled = true; }
    function on(k) { return function (v) { state.model[k] = v; changed(); }; }
    function checkbox(id, key, prop) {
      var c = el('input', { id: id, type: 'checkbox' });
      c.checked = state[prop];
      c.addEventListener('change', function () { state[prop] = c.checked; changed(); });
      return el('div', { class: 'check' }, [c, el('label', { for: id, text: t(key) })]);
    }
    test.addEventListener('click', function () {
      test.disabled = true;
      test.textContent = t('testing');
      api('/api/setup/test-model', { model: state.model, confirmLocalNetwork: state.confirmLocalNetwork, confirmCloudLab: state.confirmCloudLab }).then(function (r) {
        test.disabled = false;
        test.textContent = t('test');
        box.textContent = '';
        if (r.status !== 200) {
          if (r.status === 401) { session = null; showCode(null, t('err.unauthorized')); return; }
          box.appendChild(message('bad', errorText(r.body)));
          return;
        }
        modelOk = !hasFail(r.body.checks);
        next.disabled = !modelOk;
        box.appendChild(results(r.body.checks));
      });
    });
    next.addEventListener('click', function () { showAdmin(); });
    back.addEventListener('click', function () { showCrm(); });
    var parts = header(2, 'modelTitle').concat([el('label', { for: 'provider', text: t('provider') }), select]);
    if (!cloud) {
      parts = parts.concat(field('model-url', 'modelUrl', 'text', state.model.baseUrl, on('baseUrl')));
    }
    parts = parts.concat(field('model-name', 'modelName', 'text', state.model.model, on('model')));
    parts = parts.concat(field('model-key', cloud ? 'cloudKey' : 'modelKey', 'password', state.model.apiKey, on('apiKey'), 'new-password'));
    parts.push(cloud ? checkbox('c-cloud', 'confirmCloud', 'confirmCloudLab') : checkbox('c-net', 'confirmNetwork', 'confirmLocalNetwork'));
    parts.push(box, el('div', { class: 'row' }, [back, test, next]));
    render(parts);
  }

  function showAdmin(problem) {
    var box = el('div');
    if (problem) { box.appendChild(message('bad', problem)); }
    var back = el('button', { type: 'button', class: 'secondary', text: t('back') });
    var finish = el('button', { type: 'button', text: t('finish') });
    back.addEventListener('click', function () { showModel(); });
    finish.addEventListener('click', function () {
      finish.disabled = true;
      finish.textContent = t('saving');
      var body = {
        crm: state.crm,
        model: { provider: state.model.provider, baseUrl: state.model.baseUrl, model: state.model.model, apiKey: state.model.apiKey || null },
        serviceUrl: state.serviceUrl,
        adminAccount: state.adminAccount,
        confirmLocalNetwork: state.confirmLocalNetwork,
        confirmCloudLab: state.confirmCloudLab
      };
      api('/api/setup/complete', body).then(function (r) {
        if (r.status === 200) { showDone(); return; }
        if (r.status === 401) { session = null; showCode(null, t('err.unauthorized')); return; }
        showAdmin(errorText(r.body));
      });
    });
    render(header(3, 'adminTitle').concat(
      field('admin', 'adminLabel', 'text', state.adminAccount, function (v) { state.adminAccount = v; }),
      field('svc-url', 'serviceUrl', 'text', state.serviceUrl, function (v) { state.serviceUrl = v; }),
      [el('p', { class: 'hint', text: t('serviceHelp') }), box, el('div', { class: 'row' }, [back, finish])]));
  }

  function showDone() {
    render(header(0, 'doneTitle').concat([el('p', { text: t('doneText') })]));
    var tries = 0;
    var timer = setInterval(function () {
      tries++;
      fetch('/api/setup/state').then(function (r) {
        if (r.status === 404 || (r.ok && tries > 3 && r.headers.get('content-type') && r.headers.get('content-type').indexOf('json') < 0)) {
          clearInterval(timer);
          location.href = '/';
        }
      }).catch(function () { });
      if (tries > 90) { clearInterval(timer); }
    }, 2000);
  }

  api('/api/setup/state', undefined, 'GET').then(function (r) {
    if (r.status === 404) { location.href = '/'; return; }
    showCode(r.body, r.status === 0 ? t('err.network') : null);
  });
})();
