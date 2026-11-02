const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..');
const lab = JSON.parse(fs.readFileSync(path.join(root, 'tools', 'lab-data.json'), 'utf8'));
const customers = lab.tables.accounts.filter((a) => a.emailaddress1).map((a) => a.name);

let seed = 20261020;
/** Returns a pseudo-random number from a seeded generator. */
const rnd = () => { seed = (seed * 1664525 + 1013904223) % 4294967296; return seed / 4294967296; };
/** Picks a pseudo-random item from a list. */
const pick = (list) => list[Math.floor(rnd() * list.length)];
/** Returns a pseudo-random integer in an inclusive range. */
const int = (min, max) => min + Math.floor(rnd() * (max - min + 1));
/** Left-pads a number with zeros to a width. */
const pad = (n, w) => String(n).padStart(w, '0');
/** Builds a date string from a year and a day of year. */
const dateOf = (year, dayOfYear) => { const d = new Date(Date.UTC(year, 0, 1) + dayOfYear * 86400000); return d.toISOString().slice(0, 10); };

const options = {
  marka: ['Zephyr', 'Boreal', 'Calypso', 'Drakon', 'Ember', 'Fjord', 'Gale'],
  kasa: ['Sedan', 'Hatchback', 'SUV', 'Minivan', 'Pikap'],
  yakit: ['Benzin', 'Dizel', 'Elektrik', 'Hibrit'],
  ozellikler: ['Sunroof', 'Deri koltuk', 'Geri görüş kamerası', 'Çeki demiri'],
  islem: ['Bakım', 'Onarım', 'Lastik', 'Kaza'],
};
/** Returns a numeric option value for a label from a list. */
const value = (list, label) => 100000000 + list.indexOf(label);

const modelNames = [['Zephyr S1', 'Zephyr'], ['Zephyr S2', 'Zephyr'], ['Boreal X3', 'Boreal'], ['Boreal X5', 'Boreal'], ['Calypso 100', 'Calypso'],
  ['Calypso 200', 'Calypso'], ['Drakon GT', 'Drakon'], ['Ember Mini', 'Ember'], ['Fjord Cargo', 'Fjord'], ['Gale 7', 'Gale']];
const models = modelNames.map(([name, marka]) => ({ name, marka, yil: int(2019, 2026), kasa: pick(options.kasa), motor: Math.round((1 + rnd() * 2) * 10) / 10 }));

const accessories = ['Çatı barı', 'Paspas seti', 'Kar zinciri', 'Bebek koltuğu', 'Çeki demiri', 'Navigasyon', 'Park sensörü', 'Sis farı'];

const cityCodes = ['34', '06', '35', '16', '07'];
const letters = ['TST', 'DNM', 'ORN'];
const vehicles = [];
for (let i = 0; i < 60; i++) {
  const features = options.ozellikler.filter(() => rnd() < 0.35);
  vehicles.push({
    plaka: `${cityCodes[i % cityCodes.length]} ${letters[i % letters.length]} ${pad(i + 1, 3)}`,
    model: pick(models).name,
    musteri: customers[i % customers.length],
    yakit: pick(['Benzin', 'Benzin', 'Dizel', 'Dizel', 'Dizel', 'Elektrik', 'Hibrit']),
    garantili: rnd() < 0.45,
    kilometre: int(5, 180) * 1000,
    alimTarihi: dateOf(int(2019, 2026), int(0, 330)),
    ozellikler: features,
    aksesuarlar: accessories.filter(() => rnd() < 0.2).slice(0, 3),
    aciklama: rnd() < 0.3 ? 'Uydurma demo araç (tohum verisi).' : null,
  });
}

const services = [];
for (let i = 0; i < 150; i++) {
  const vehicle = vehicles[i % vehicles.length];
  const tur = pick(options.islem);
  services.push({
    baslik: `Servis ${pad(i + 1, 3)} - ${tur}`,
    plaka: vehicle.plaka,
    tarih: dateOf(int(2024, 2026), int(0, 330)),
    tutar: int(6, 240) * 50,
    tur,
    tamamlandi: rnd() < 0.75,
  });
}

const data = { options, models, accessories, vehicles, services };
fs.writeFileSync(path.join(root, 'tools', 'custom-data.json'), JSON.stringify(data, null, 2) + '\n');

const byPlate = Object.fromEntries(vehicles.map((v) => [v.plaka, v]));
/** Returns the plate field value of a vehicle record, used by the test to read the Turkish-named column. */
const plateOf = (s) => s.plaka;
const customer = customers[0];
const customerPlates = new Set(vehicles.filter((v) => v.musteri === customer).map((v) => v.plaka));
const custServices = services.filter((s) => customerPlates.has(s.plaka));
const sample = vehicles.find((v) => v.aksesuarlar.length >= 2);
const dieselCount = vehicles.filter((v) => v.yakit === 'Dizel').length;
const warrantyCount = vehicles.filter((v) => v.garantili).length;
const sunroof = vehicles.filter((v) => v.ozellikler.includes('Sunroof'));
const expensive = services.filter((s) => s.tutar > 8000);
const openMaintenance = services.filter((s) => s.tur === 'Bakım' && !s.tamamlandi);
const in2026 = new Set(services.filter((s) => s.tarih.startsWith('2026')).map((s) => s.plaka));
const zephyrS1 = vehicles.filter((v) => v.model === 'Zephyr S1');
const istanbul = vehicles.filter((v) => v.plaka.startsWith('34'));
const countBy = {};
for (const s of services) countBy[s.plaka] = (countBy[s.plaka] || 0) + 1;
const top = Object.entries(countBy).sort((a, b) => b[1] - a[1]);
const topTied = top.filter(([, n]) => n === top[0][1]);

const cases = [
  { id: 'k01-plaka-34', question: 'Plakası 34 ile başlayan araçları getir', expect: { finalKind: 'answer', entity: 'new_arac', rowCount: istanbul.length, mustContain: [istanbul[0].plaka] } },
  { id: 'k02-dizel', question: 'Yakıt tipi dizel olan araçları getir', expect: { finalKind: 'answer', entity: 'new_arac', rowCount: dieselCount } },
  { id: 'k03-garantili-en', question: 'Show the vehicles that are still under warranty', expect: { finalKind: 'answer', entity: 'new_arac', rowCount: warrantyCount } },
  { id: 'k04-model-musterileri', question: 'Zephyr S1 modelindeki araçların plakalarını getir', expect: { finalKind: 'answer', entity: 'new_arac', rowCount: zephyrS1.length, mustContain: zephyrS1.slice(0, 1).map(plateOf) } },
  { id: 'k05-musteri-servisleri', question: `${customer} firmasının araçlarının servis kayıtlarını getir`, expect: { finalKind: 'answer', entity: 'new_servis_kaydi', rowCount: custServices.length } },
  { id: 'k06-tutar-en', question: 'Show service records with a cost over 8000', expect: { finalKind: 'answer', entity: 'new_servis_kaydi', rowCount: expensive.length } },
  { id: 'k07-sunroof', question: 'Sunroof özelliği olan araçları getir', expect: { finalKind: 'answer', entity: 'new_arac', rowCount: sunroof.length }, note: 'Cok secimli secenek kumesi; CRM surumu desteklemiyorsa bu vaka atlanir.' },
  { id: 'k08-bakim-tamamlanmamis', question: 'Tamamlanmamış bakım servis kayıtlarını getir', expect: { finalKind: 'answer', entity: 'new_servis_kaydi', rowCount: openMaintenance.length } },
  { id: 'k09-aksesuarlar', question: `${sample.plaka} plakalı aracın aksesuarlarını getir`, expect: { finalKind: 'answer', entity: 'new_aksesuar', rowCount: sample.aksesuarlar.length, mustContain: [sample.aksesuarlar[0]] }, note: 'Coktan coga iliski (arac <-> aksesuar).' },
  { id: 'k10-2026-servis-araclari', question: '2026 yılında servis kaydı olan araçların plakalarını getir', expect: { finalKind: 'answer', entity: 'new_arac', rowCount: in2026.size } },
];
for (const c of cases) c.answers = { columns: 'default' };
fs.writeFileSync(path.join(root, 'eval', 'custom-cases.json'), JSON.stringify(cases, null, 2) + '\n');

console.log('Written tools/custom-data.json and eval/custom-cases.json');
console.log(`  models ${models.length}, accessories ${accessories.length}, vehicles ${vehicles.length}, services ${services.length}, customers used ${customers.length}`);
console.log(`  expectations: 34-plates ${istanbul.length}, diesel ${dieselCount}, warranty ${warrantyCount}, sunroof ${sunroof.length}, cost>8000 ${expensive.length}, open maintenance ${openMaintenance.length}, vehicles with 2026 service ${in2026.size}`);
console.log(`  customer for k05: ${customer} (${customerPlates.size} vehicles, ${custServices.length} service records); sample vehicle for k09: ${sample.plaka} (${sample.aksesuarlar.length} accessories)`);
