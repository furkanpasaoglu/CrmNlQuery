# Yazıcı ve tarayıcı kurulumu
Doküman No: BT-REH-12
Revizyon: 1.4
Sorumlu Birim: Bilgi Teknolojileri Saha Destek
Yürürlük Tarihi: 15.01.2025

1. AMAÇ
Bu rehber, kurum içindeki yazıcı ve tarayıcı cihazlarının standart yöntemle kurulmasını, ağ üzerinde doğru şekilde tanımlanmasını ve kullanıcıların cihazlara sorunsuz erişmesini sağlamak için hazırlanmıştır.

2. KAPSAM
- Ofis tipi lazer ve çok fonksiyonlu yazıcılar
- Ağa bağlı tarayıcılar
- Kiosk tipi yazdırma ünitelerinin yapılandırılması (ayrı rehberdedir)

3. ÖN HAZIRLIK
- Cihazın IP adresi ağ yöneticisinden alınmalıdır.
- Sürücü paketi yalnızca kurumsal depodan indirilmelidir.
- Cihaz yönetici şifresi değiştirilmemişse, varsayılan şifre yerine kurumsal şifre kullanılmalıdır.

4. YAZICI KURULUM ADIMLARI
- Cihazı ağ kablosuyla veya kablosuz bağlantıyla ağa bağlayın.
- Cihazın kontrol panelinden IP adresini kontrol edin.
- Kullanıcı bilgisayarında Yazıcılar ve Tarayıcılar bölümüne girin.
- Ağ yazıcısı ekle seçeneğiyle cihazı IP adresi üzerinden bulun.
- Kurumsal sürücüyü yükleyin ve test sayfası yazdırın.
- Cihaz adını kat ve oda numarasına göre adlandırın (örn. K2-O14-LZR01).

5. TARAYICI KURULUM ADIMLARI
- Tarayıcının ağ ayarlarından taramanın kayıt yeri olarak paylaşımlı klasör belirleyin.
- Tarama hedefi için kullanıcının yetkisini kontrol edin.
- E-posta ile tarama özelliği kullanılacaksa SMTP bilgileri yalnızca BT ekibi tarafından girilir.
- Test taraması yapın ve çıktının doğru klasöre düştüğünü doğrulayın.

6. YAYGIN SORUNLAR
- Yazıcı çevrimdışı görünüyor: Ağ kablosunu ve IP çakışmasını kontrol edin.
- Çıktılar boş geliyor: Toner veya drum durumunu kontrol edin, sürücüyü güncelleyin.
- Tarama klasöre düşmüyor: Paylaşım yetkisini ve hedef klasör yolunu doğrulayın.
- Kağıt sıkışması: Cihaz kılavuzuna göre temizleyin, tekrarlanırsa talep açın.

7. GÜVENLİK
- Gizli belgeler için PIN korumalı yazdırma özelliğini kullanın.
- Kullanılmayan protokoller (FTP, eski SNMP sürümleri) kapatılmalıdır.
- Cihazların firmware güncellemeleri BT onayıyla yapılır.

8. DESTEK
Sorun çözülemezse destek hattından talep açılmalı, cihaz etiket numarası ve hata kodu belirtilmelidir.
