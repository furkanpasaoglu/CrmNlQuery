# Yedekleme politikası
Doküman No: BT-POL-07
Revizyon: 3.0
Sorumlu Birim: Bilgi Güvenliği ve Altyapı Müdürlüğü
Onaylayan: Genel Müdür Yardımcısı

1. AMAÇ
Kurumsal verilerin kayıp, bozulma veya siber saldırı durumlarında geri dönüştürülebilmesini sağlamak ve iş sürekliliğini desteklemek amacıyla bu politika hazırlanmıştır.

2. KAPSAM
- Merkez veri merkezindeki sunucular
- Dosya paylaşım sunucuları ve ortak klasörler
- Veritabanı sunucuları
- Bulut ortamında barındırılan kurumsal hizmetler
- Kullanıcı bilgisayarlarındaki yerel veriler kapsam dışıdır, ancak kullanıcılar kritik dosyalarını ortak alana taşımakla yükümlüdür.

3. YEDEKLEME TÜRLERİ
- Tam yedek: Haftada bir, pazar günleri gece 02:00
- Artımlı yedek: Her gece 02:00
- Veritabanı işlem günlükleri: 15 dakikada bir

4. SAKLAMA SÜRELERİ
- Günlük yedekler: 14 gün
- Haftalık yedekler: 8 hafta
- Aylık yedekler: 12 ay
- Yasal saklama gerektiren kayıtlar, ilgili mevzuatta belirtilen süre boyunca saklanır.

5. YEDEK ORTAMLARI
- Birincil yedekler yerel ağda ayrı bir depolama ünitesinde tutulur.
- İkincil kopya coğrafi olarak farklı bir lokasyona şifreli olarak gönderilir.
- Yedek ortamları yalnızca yetkili personel tarafından erişilebilir.

6. GERİ YÜKLEME TESTLERİ
- Üç ayda bir örnek geri yükleme testi yapılır.
- Test sonuçları kayıt altına alınır ve yönetime raporlanır.
- Başarısız testlerde kök neden analizi 5 iş günü içinde tamamlanır.

7. HEDEFLER
- RTO (Kurtarma süresi hedefi): Kritik sistemler için 4 saat
- RPO (Veri kaybı toleransı): Veritabanları için 15 dakika, diğer sistemler için 24 saat

8. SORUMLULUKLAR
- Altyapı ekibi: Yedeklerin alınması, izlenmesi ve testlerin yapılması
- Bilgi güvenliği ekibi: Şifreleme anahtarlarının yönetimi
- Sistem sahipleri: Kritik veri listesinin güncel tutulması

9. İHLAL DURUMU
Bu politikaya uyulmaması disiplin sürecini başlatır. Yedekleme işlemlerinde oluşan aksaklıklar derhal güvenlik birimine bildirilmelidir.

10. GÖZDEN GEÇİRME
Bu politika yılda bir kez veya önemli bir altyapı değişikliğinde gözden geçirilir.
