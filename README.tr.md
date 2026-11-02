<h1 align="center">CrmNlQuery</h1>

<p align="center">
  <b>Şirket içi Dynamics 365'inize sade bir dille soru sorun.<br>Verileriniz ağınızdan dışarı çıkmaz.</b>
</p>

<p align="center">
  <a href="https://github.com/furkanpasaoglu/CrmNlQuery/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/furkanpasaoglu/CrmNlQuery/actions/workflows/ci.yml/badge.svg"></a>
  <img alt="Lisans: MIT" src="https://img.shields.io/badge/lisans-MIT-blue.svg">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4.svg">
  <img alt="Docker" src="https://img.shields.io/badge/docker-compose-2496ED.svg">
  <img alt="Dynamics 365 on-premises" src="https://img.shields.io/badge/Dynamics%20365-on--premises-002050.svg">
  <img alt="Yerel modeller" src="https://img.shields.io/badge/modeller-Ollama%20%7C%20vLLM-black.svg">
</p>

<p align="center">
  <a href="README.md">English</a> ·
  <a href="docs/screenshots.md">Ekran görüntüleri</a> ·
  <a href="docs/deployment.md">Kurulum</a> ·
  <a href="docs/architecture.md">Mimari</a> ·
  <a href="docs/specs/README.md">Tasarım spec'leri</a> ·
  <a href="docs/features.md">Özellikler</a>
</p>

<p align="center">
  <img src="docs/images/tour-chat.gif" alt="Dynamics 365 içindeki sohbet penceresi: tablo, grafik, özet, e-posta taslağı ve rapor" width="900">
</p>

---

## Ne yapar

CrmNlQuery, Dynamics 365 (on-premises) sayfalarına bir sohbet penceresi ekler. İş analisti soruyu yazar; **yerel** bir dil modeli bunu doğrulanmış, salt okur bir FetchXML sorgusuna çevirir; sorgu **o kullanıcı adına** çalışır, yani kullanıcının neyi göreceğine yine CRM'in kendi güvenliği karar verir. Cevap tablo, grafik ya da özet olarak gelir.

| Soru | Cevap |
|---|---|
| `Show accounts in London` | her süzgecin sade dille açıklandığı bir tablo |
| `Show the number of cases by priority as a chart` | tablo görünümü ve PNG indirmesi olan grafik |
| `Summarize Brightwave Appliances Ltd` | sabit sorgulardan kodla üretilen müşteri özeti |
| `Show the open opportunities of this customer` | formda açık olan kayıt için cevap |
| `Show urgent cases` | şirketinizin kendi terimi, kodla uygulanır ve denetlenir |
| `İstanbul'daki hesapları getir` | aynısı, Türkçe |
| `Write a short follow-up e-mail to Freya Testwell` | değerleri kodla doldurulan taslak (hiçbir şey gönderilmez) |
| `open the Neglected Cases report` | CRM raporunu açan düğme |

## Neden var

Birçok şirket Dynamics 365'i hâlâ şirket içinde çalıştırıyor, çünkü verisi dışarı çıkamıyor. Bulut tabanlı yardımcılar onlar için seçenek değil, rapor hazırlama da birkaç uzmanda kalıyor.

|  |  |
|---|---|
| 🏠 **Yerel modeller** | Kendi donanımınızda Ollama ya da vLLM. Bulut modeli mümkün ama açıkça izin vermedikçe kapalıdır ve yalnızca hassas olmayan veri içindir. |
| 🙈 **Model satır verisini görmez** | Soruyu ve şemayı görür. Kayıtları, oturum açmış kullanıcı adına kod okur. |
| 🧑‍⚖️ **İnsan onayı** | CRM'e yazan her işlem önce önizlenir ve açık bir tıklama ister. Servis hesabı tasarım gereği salt okurdur. |
| 🔎 **Denetlenebilir** | Her okuma (kim, hangi tablolar, kaç satır) veriyi saklamadan kaydedilir. |

## Öne çıkanlar

- Türkçe ve İngilizce sorular için **doğal dilden FetchXML'e** çeviri; CRM'e ulaşmadan önce izin listesi dışındaki her şeyi reddeden bir plan doğrulayıcı
- **Çok adımlı sorular:** koşullar ("yoksa ...") ve bağımlı adımlar ("en çok açık fırsatı olan hesabın kişileri")
- **Devam soruları ve bağlam:** "sadece İstanbul'dakiler", "bu hesabın kişileri"
- **Açıklanabilir:** her cevap neye göre süzdüğünü söyler; süzgeçler modele yeniden gitmeden kaldırılabilir ya da değiştirilebilir
- **Müşteri özeti, grafik, e-posta taslağı, rapor bulma, iş sözlüğü, bilgi tabanı (RAG)**
- **CRM sayfalarında sohbet penceresi:** sohbetler, durdur düğmesi, slash komutları, geri bildirim, açık/koyu tema
- **React yönetim paneli:** kullanıcı ve roller, veri izin listesi, denetim kaydı, kalite kontrolü, ayarlar, dışa aktarma
- Cevapları canlı veride referans sorgularla karşılaştıran **değerlendirme çerçevesi**

Tam liste [docs/features.md](docs/features.md) dosyasındadır (İngilizce).

## Nasıl çalışır

```
 CRM sayfası                     CrmNlQuery servisi                           Dynamics 365
┌──────────────┐     soru      ┌─────────────────────────────────────┐
│ sohbet       │ ────────────► │ analiz ─► ad çözme ─► plan          │
│ penceresi    │               │ (model)   (CRM okuma)  (model)      │
│              │               │              │                      │
│              │  tablo/grafik │      doğrulama (kod, izin listeleri)│  FetchXML, kullanıcı adına
│              │ ◄──────────── │              ▼                      │ ───────────────────────►
└──────────────┘               │   çalıştır ─► tablo / grafik üret   │ ◄───────────────────────
                               └─────────────────────────────────────┘     satırlar (modele gitmez)
```

## Güvenlik modeli

| İlke | Nasıl |
|---|---|
| Kullanıcıyı CRM tanır | Sohbet penceresi, oturum açmış kullanıcı olarak tek kullanımlık bir görev oluşturur; servis onu kimin oluşturduğunu okur. CRM insanları nasıl doğrularsa doğrulasın ikinci bir parola yoktur. |
| Her okuma çağıran adına çalışır | CRM taklidi; boş kimlik asla servis hesabına düşmez. |
| En az yetkili servis hesabı | İzin listesindeki tablolara okuma; kendi çağrıları da izin listeli bir okuma politikasından geçer. |
| CRM'in üstünde ikinci kapı | Yöneticiler hangi tablo ve alanların sorulabileceğini seçer; hassas görünen alanlar kapalı başlar. |
| Kotalar ve acil durdurma | Kullanıcı başına sınırlar, eşzamanlılık, uçtan uca zaman aşımı, sormayı duraklatan tek anahtar. |
| Sıkı web hijyeni | Satır içi betik yok (CSP), siteler arası istek koruması, boyut sınırları. |

## Hızlı başlangıç

Gerekenler: Compose'lu Docker, Web API'si açık bir Dynamics 365 on-premises organizasyonu, en az yetkili bir CRM servis hesabı ve Ollama (ya da desteklenen başka bir model sunucusu).

```bash
cp .env.example .env          # POSTGRES_PASSWORD'e uzun rastgele bir değer yazın; CRM_PASSWORD boş kalsın
docker compose up -d --build
docker compose logs app       # tek seferlik kurulum kodunu yazar
```

`http://localhost:5080/setup` adresini açın, kodu girin ve sihirbazı izleyin. Bir şey kaydedilmeden önce CRM bağlantısını ve modeli dener, sırları şifreli saklar. Sonra `tools/Install-ChatWidget.ps1` ile sohbet penceresini CRM'e ekleyin. CRM servis hesabı dahil tüm kılavuz [docs/deployment.md](docs/deployment.md) dosyasındadır (İngilizce).

<p align="center">
  <img src="docs/images/tour-setup.gif" alt="İlk açılış kurulum sihirbazı" width="560">
</p>

## Yönetim paneli

<p align="center">
  <img src="docs/images/tour-admin.gif" alt="Yönetim paneli: veri erişimi, sözlük, belgeler, kalite kontrolü, roller, denetim kaydı, erişim kaydı, tasarım asistanı, ayarlar" width="900">
</p>

<table>
  <tr>
    <td width="50%"><img src="docs/images/admin-data-access.jpg" alt="Veri erişimi"><br><sub><b>Veri erişimi.</b> Aracın hangi tablo ve alanları kullanabileceğini, CRM güvenliğinin üstünde seçin.</sub></td>
    <td width="50%"><img src="docs/images/admin-quality-check.jpg" alt="Kalite kontrolü"><br><sub><b>Kalite kontrolü.</b> Şemanızdan üretilen bir kabul seti.</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/admin-audit-trail.jpg" alt="Denetim kaydı"><br><sub><b>Denetim kaydı.</b> Kullanıcı, rol ve ayarlardaki her değişiklik.</sub></td>
    <td width="50%"><img src="docs/images/admin-design-assistant.jpg" alt="Tasarım asistanı"><br><sub><b>Tasarım asistanı.</b> Yazılı bir özelleştirme tasarımı; CRM'de hiçbir şey oluşturmaz.</sub></td>
  </tr>
</table>

Daha fazla görüntü: [docs/screenshots.md](docs/screenshots.md).

## Belgeler

| | |
|---|---|
| [docs/deployment.md](docs/deployment.md) | Docker dağıtımı, kurulum sihirbazı, CRM servis hesabı |
| [docs/architecture.md](docs/architecture.md) | Katmanlar, bağımlılık kuralları, istek akışı |
| [docs/specs/](docs/specs/README.md) | Özellik başına bir tasarım spec'i |
| [docs/features.md](docs/features.md) | Ürünün yaptıkları |
| [eval/README.md](eval/README.md) | Değerlendirme paketleri |
| [tools/README.md](tools/README.md) | Kurulum ve ölçüm betikleri |

## Depo yapısı

| Yol | İçerik |
|---|---|
| `src/` | Servis: `Domain`, `Application`, `Infrastructure`, `Composition`, `Web` (HTTP ve sohbet penceresi), `Eval` (değerlendirme çalıştırıcısı) |
| `admin-ui/` | Yönetim paneli |
| `tests/` | Birim, sözleşme ve mimari testleri; widget ve katalog denetimleri |
| `eval/` | Değerlendirme paketleri: beklenen sonuçlu sorular |
| `tools/` | Kurulum ve değerlendirme betikleri |
| `docs/` | Tasarım spec'leri, mimari, dağıtım kılavuzu, özellik listesi |

## Geliştirme

```bash
dotnet test --filter "Category!=Live"            # birim testleri; "Live" testleri CRM veya PostgreSQL ister
for f in tests/widget/*.test.js; do node "$f"; done
cd admin-ui && npm ci && npm run build           # src/CrmNlQuery.Web/wwwroot/admin-next klasörüne yazılır
```

Proje önce spec ile geliştirilir: her özellik `docs/specs` içinde kısa bir tasarım belgesi olarak başlar, sonra test, sonra kod gelir. Kodda gömülü kullanıcı metni yasaktır; her ileti İngilizce ve Türkçe yan yana bir katalogda durur ve bir test bunu zorlar.

## Durum

- Dynamics 365 (on-premises) 9.0 üzerinde geliştirildi ve doğrulandı. 8.2 uyumluluk hedefidir.
- Sohbetten kayıt oluşturma uygulandı ama varsayılan olarak kapalıdır; servis hesabına ek yetki ister.
- Servisten CRM'e Windows (NTLM) kimlik doğrulaması desteklenir; yalnızca AD FS ile açılan uçlar henüz desteklenmez.
- Tek servis örneği çalıştırın. Sınırlar için [docs/deployment.md](docs/deployment.md#9-operational-limits).

## Lisans

[MIT](LICENSE)
