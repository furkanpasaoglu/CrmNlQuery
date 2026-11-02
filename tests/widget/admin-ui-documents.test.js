const assert = require('assert');
const path = require('path');
const { pathToFileURL } = require('url');

(async () => {
  const m = await import(pathToFileURL(path.join(__dirname, '..', '..', 'admin-ui', 'src', 'lib', 'documentChecks.ts')).href);

  assert.deepStrictEqual(m.checkDocument('KOBİ müşteri, çalışan sayısı (numberofemployees) 250\'den az olan hesaptır.'), []);
  assert.deepStrictEqual(m.checkDocument('Mali yıl takvim yılıdır. Q4 = Ekim-Aralık. 2026 Q4 1 Ekim 2026 ile 31 Aralık 2026 arasıdır.'), []);

  assert.deepStrictEqual(m.checkDocument('a'.repeat(m.LONG_BODY)), []);
  assert.deepStrictEqual(m.checkDocument('a'.repeat(m.LONG_BODY + 1)), ['long']);

  assert.ok(m.checkDocument('TC: 12345678901').includes('nationalId'));
  assert.ok(!m.checkDocument('Sipariş 123456789012345').includes('nationalId'));
  assert.ok(!m.checkDocument('Tutar 8000 TL, 2026 yılı').includes('nationalId'));

  assert.ok(m.checkDocument('IBAN TR33 0006 1005 1978 6457 8413 26').includes('iban'));
  assert.ok(m.checkDocument('Ara: 0555 123 45 67').includes('phone'));
  assert.ok(m.checkDocument('Ara: +90 555 123 45 67').includes('phone'));
  assert.ok(m.checkDocument('Yazın: ali.veli@firma.com').includes('email'));
  assert.ok(!m.checkDocument('Plaka 34 ABC 123, model yılı 2025').includes('phone'));

  assert.deepStrictEqual(m.checkFile('kobi.md', 500), { ok: true });
  assert.deepStrictEqual(m.checkFile('NOTLAR.TXT', 500), { ok: true });
  assert.deepStrictEqual(m.checkFile('rapor.docx', 500), { ok: false, reason: 'type' });
  assert.deepStrictEqual(m.checkFile('x.md', 0), { ok: false, reason: 'empty' });
  assert.deepStrictEqual(m.checkFile('x.md', m.MAX_FILE_BYTES + 1), { ok: false, reason: 'tooBig' });

  assert.strictEqual(m.titleFromFileName('kobi_tanimi.md'), 'kobi tanimi');
  assert.strictEqual(m.titleFromFileName('Notlar'), 'Notlar');
  assert.strictEqual(m.cleanText('﻿a\r\nb\r\n'), 'a\nb');

  console.log('admin-ui-documents testi gecti');
})().catch((e) => { console.error(e); process.exit(1); });
