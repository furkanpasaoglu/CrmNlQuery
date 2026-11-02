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

## Kullanılan teknolojiler

| Alan | Teknolojiler |
|---|---|
| Servis | C# / .NET 10, ASP.NET Core (minimal API), Npgsql, Serilog |
| Veri | pgvector'lü PostgreSQL 17, AES-GCM ile şifreli ayarlar |
| Yapay zekâ | Ollama (varsayılan), vLLM (OpenAI uyumlu), isteğe bağlı Claude API, bge-m3 gömmeleri, JSON şemalı yapılandırılmış çıktı |
| CRM | Dynamics 365 Web API (OData), FetchXML, taklit, Windows (NTLM) kimlik doğrulama |
| Sohbet penceresi | Saf JavaScript, Shadow DOM, Canvas ile grafik (kütüphanesiz) |
| Yönetim paneli | React 19, TypeScript, Vite, Mantine 9, TanStack Query, React Router, Recharts |
| Kalite | xUnit (yaklaşık 2.900 test), mimari ve sözleşme testleri, Node denetimleri, GitHub Actions |
| Dağıtım | Docker, Docker Compose, PowerShell kurulum betikleri |

## Yapay zekâ kavramları

| Kavram | Nasıl kullanılıyor | Tasarım spec'i |
|---|---|---|
| **RAG** | pgvector'de bge-m3 gömmeleri, hibrit arama (vektör + tam metin, karşılıklı sıra birleştirmesiyle), parçalama ve benzerlik eşiği; isteme yalnızca ilgili parçalar girer | [bilgi tabanı](docs/specs/knowledge-base.md) |
| **Human in the loop (insan onayı)** | Kayıt oluşturma önizleme ve açık tıklama ister; e-posta taslakları gönderilmez; kalite seti yalnızca yönetici onayıyla büyür | [kayıt oluşturma](docs/specs/record-creation.md), [e-posta taslağı](docs/specs/email-draft.md), [kalite kontrolü](docs/specs/quality-checks.md) |
| **Guardrails (korkuluklar)** | Plan doğrulayıcı, izin listeleri, veri izin politikası, kotalar, acil durdurma ve günlük bulut token bütçesi | [korkuluklar](docs/specs/guardrails-and-limits.md), [veri izin politikası](docs/specs/data-access-policy.md) |
| **İstem enjeksiyonu savunması** | CRM'den gelen metin tek satıra indirilir, gizli karakterlerden arındırılır ve veri olarak tırnaklanır; model çıktısı kodla doğrulanır | [korkuluklar](docs/specs/guardrails-and-limits.md) |
| **Yapılandırılmış çıktı** | Her model çağrısında JSON şeması zorlanır (Ollama `format`, vLLM `response_format`, Claude yapılandırılmış çıktı) | [doğal dil sorgusu](docs/specs/natural-language-query.md) |
| **Text-to-query** | Üç adımlı iş akışı: analiz, bölme, planlama; koşullu ve bağımlı çok adımlı planlar | [doğal dil sorgusu](docs/specs/natural-language-query.md), [koşullu planlar](docs/specs/conditional-plans.md), [bağımlı adımlar](docs/specs/dependent-steps.md) |
| **Semantik katman** | İş sözlüğü şirket terimlerini, kodun zorladığı süzgeçlere çevirir | [iş sözlüğü](docs/specs/business-glossary.md) |
| **Bağlam mühendisliği** | Kural tabanlı bağlam budama, sorunun kendi tablolarından örnekler, istem boyutu ölçümü | [istem yapısı](docs/specs/prompt-structure.md), [bağlam penceresi](docs/specs/prompt-size-and-context-window.md) |
| **Ön ek önbelleğine uygun istemler** | Sabit kısımlar önce, değişenler sonda; model sunucusu önbelleğini yeniden kullanabilir; ayrıca kendi plan ve analiz önbelleklerimiz | [istem yapısı](docs/specs/prompt-structure.md) |
| **Değerlendirme (evals)** | Referans sorgu modu (beklenen sonuç canlı veride hesaplanır), şemadan üretilen kabul seti, regresyon raporları, zamanlanmış koşular, yük ve gecikme ölçümü | [referans sorgular](docs/specs/reference-query-evaluation.md), [kalite kontrolü](docs/specs/quality-checks.md), [başlangıç seti](docs/specs/starter-acceptance-set.md) |
| **Yerel, modelden bağımsız çıkarım** | Ollama, vLLM ve isteğe bağlı bulut modeli tek arayüzün arkasında; düşünme, bağlam boyutu ve keep-alive için adım başına ayar | [vLLM sağlayıcısı](docs/specs/vllm-provider.md) |
| **Tasarımdan gelen gizlilik** | Model satır verisini görmez; her okuma verinin kendisi olmadan kaydedilir | [veri erişim kaydı](docs/specs/data-access-log.md), [veri izin politikası](docs/specs/data-access-policy.md) |

**Bilerek kullanılmayanlar:** otonom ajanlar ve function calling (hat deterministiktir: model önerir, kod karar verir), fine-tuning ve LLM-as-judge (cevaplar bunun yerine referans sorgularla denetlenir).

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
