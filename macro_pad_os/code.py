import board
import busio
import adafruit_ssd1306
import time
import digitalio
import rotaryio
import neopixel
import json
import os
import sys
import supervisor

# --- KLAVYE VE MEDYA KÜTÜPHANELERİ ---
import usb_hid
from adafruit_hid.keyboard import Keyboard
from adafruit_hid.keycode import Keycode
from adafruit_hid.consumer_control import ConsumerControl
from adafruit_hid.consumer_control_code import ConsumerControlCode
# Türkçe Klavye Düzeni
from keyboard_layout_win_tr import KeyboardLayout

# --- 1. Başlangıç Temizliği ---
pixels = neopixel.NeoPixel(board.GP10, 10, brightness=0.5, auto_write=False)
pixels.fill((0, 0, 0))
pixels.show()

# Klavye ve Medya Kurulumu
kbd = Keyboard(usb_hid.devices)
layout = KeyboardLayout(kbd)
cc = ConsumerControl(usb_hid.devices)

MEDYA_KODLARI = {
    "PLAY/PAUSE": ConsumerControlCode.PLAY_PAUSE,
    "NEXT TRACK": ConsumerControlCode.SCAN_NEXT_TRACK,
    "PREVIOUS TRACK": ConsumerControlCode.SCAN_PREVIOUS_TRACK,
    "MUTE": ConsumerControlCode.MUTE,
    "VOLUME UP": ConsumerControlCode.VOLUME_INCREMENT,
    "VOLUME DOWN": ConsumerControlCode.VOLUME_DECREMENT,
}

# --- 2. Donanım ---
i2c = busio.I2C(board.GP29, board.GP28)
oled = adafruit_ssd1306.SSD1306_I2C(128, 64, i2c)

try:
    encoder = rotaryio.IncrementalEncoder(board.GP27, board.GP26)
    last_encoder_pos = 0
except:
    encoder = None

enc_button = digitalio.DigitalInOut(board.GP15)
enc_button.pull = digitalio.Pull.UP
enc_button_prev = True

tus_pinleri = [board.GP0, board.GP1, board.GP2, board.GP3, board.GP4, 
               board.GP5, board.GP6, board.GP7, board.GP8, board.GP9]
tuslar = []
tus_durumlari = [True] * 10
for pin in tus_pinleri:
    t = digitalio.DigitalInOut(pin)
    t.pull = digitalio.Pull.UP
    tuslar.append(t)

# --- 3. Değişkenler ---
ekran_parlaklik, led_parlaklik = 100, 50
isik_durumu, isik_modu_indeks = True, 0
uyku_modu_aktif, macropad_yazisi = True, True
su_anki_dosya = ""
aktif_profil = {"isim": "Varsayilan", "tuslar": [{"gorev": "YOK"}] * 10}

mod, yonetim_modu = 0, False 
secili_indeks, liste_indeks = 0, 0
last_interaction = time.monotonic()
last_blink, blink_state = time.monotonic(), True
uyku_durumu, needs_update = False, True

efektler = ["Mavi", "Kirmizi", "Yesil", "Beyaz"]
renkler = [(0,0,255), (255,0,0), (0,255,0), (255,255,255)]
tus_led_sirasi = [4, 3, 2, 1, 0, 5, 6, 7, 8, 9]

seri_buffer = ""

# --- 4. Fonksiyonlar ---

def keycode_cozucu(t_ismi):
    """Metin olarak verilen tuş ismini Keycode karşılığına çevirir."""
    t_ismi = t_ismi.strip().upper()
    if t_ismi in ["CTRL", "CONTROL"]: return Keycode.CONTROL
    if t_ismi == "ALT": return Keycode.ALT
    if t_ismi == "SHIFT": return Keycode.SHIFT
    if t_ismi in ["WIN", "GUI", "CMD", "WINDOWS"]: return Keycode.GUI
    if t_ismi in ["DEL", "DELETE"]: return Keycode.DELETE
    if t_ismi == "ESC": return Keycode.ESCAPE
    if t_ismi == "ENTER": return Keycode.ENTER
    if t_ismi == "SPACE": return Keycode.SPACE
    if t_ismi in ["TAB"]: return Keycode.TAB
    if t_ismi in ["BACKSPACE", "BACK_SPACE"]: return Keycode.BACKSPACE
    if t_ismi in ["INSERT"]: return Keycode.INSERT
    if t_ismi in ["PAGEUP", "PAGE_UP"]: return Keycode.PAGE_UP
    if t_ismi in ["PAGEDOWN", "PAGE_DOWN"]: return Keycode.PAGE_DOWN
    if t_ismi in ["UP", "YUKARI"]: return Keycode.UP_ARROW
    if t_ismi in ["DOWN", "ASAGI"]: return Keycode.DOWN_ARROW
    if t_ismi in ["LEFT", "SOL"]: return Keycode.LEFT_ARROW
    if t_ismi in ["RIGHT", "SAG"]: return Keycode.RIGHT_ARROW
    
    if hasattr(Keycode, t_ismi):
        return getattr(Keycode, t_ismi)
    return None

def makro_calistir(gorev_str):
    """Görevi analiz eder ve bilgisayara gönderir."""
    if not gorev_str or gorev_str == "YOK": return
    
    try:
        if gorev_str.startswith("STRING:"):
            metin = gorev_str[7:] # "STRING:" kısmını at
            layout.write(metin) # Türkçe düzende yaz
        elif gorev_str.upper() in MEDYA_KODLARI:
            cc.send(MEDYA_KODLARI[gorev_str.upper()])
        else:
            basilacak_tuslar = []
            tus_listesi = gorev_str.split('+')
            for t in tus_listesi:
                kod = keycode_cozucu(t)
                if kod: basilacak_tuslar.append(kod)
            
            if basilacak_tuslar:
                kbd.send(*basilacak_tuslar) # Kombinasyonu bas ve bırak
    except Exception as e:
        print("Makro Hatasi:", e)

def led_durumu_ayarla():
    if not isik_durumu: pixels.fill((0, 0, 0))
    else:
        pixels.brightness = led_parlaklik / 100
        pixels.fill(renkler[isik_modu_indeks])
    if yonetim_modu:
        renk = (255, 0, 0) if blink_state else (0, 0, 0)
        pixels[4], pixels[3], pixels[2] = renk, renk, renk
    pixels.show()

def ayarlari_kaydet():
    try:
        data = {"ekran_p": ekran_parlaklik, "led_p": led_parlaklik, "isik_d": isik_durumu, 
                "isik_m": isik_modu_indeks, "uyku_a": uyku_modu_aktif, "yazi_g": macropad_yazisi, "son_p": su_anki_dosya}
        with open("/settings.json", "w") as f: json.dump(data, f)
    except: pass

def profil_yukle(dosya):
    global aktif_profil, su_anki_dosya
    try:
        with open("/" + dosya, "r") as f:
            aktif_profil = json.load(f)
            su_anki_dosya = dosya
            ayarlari_kaydet()
    except: pass

def ikon_ciz(ikon_verisi, x, y):
    for i in range(24):
        for j in range(32):
            if (ikon_verisi[(i * 32 + j) // 8] >> (7 - (j % 8))) & 1: oled.pixel(x + j, y + i, 1)

icon_profile = bytearray([0x00, 0x07, 0xe0, 0x00, 0x00, 0x1f, 0xf8, 0x00, 0x00, 0x3f, 0xfc, 0x00, 0x00, 0x3f, 0xfc, 0x00, 0x00, 0x1f, 0xf8, 0x00, 0x00, 0x07, 0xe0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x07, 0xff, 0xff, 0xe0, 0x1f, 0xff, 0xff, 0xf8, 0x3f, 0xff, 0xff, 0xfc, 0x3f, 0xff, 0xff, 0xfc, 0x7f, 0xff, 0xff, 0xfe, 0x7f, 0xff, 0xff, 0xfe, 0x7f, 0xff, 0xff, 0xfe, 0x7f, 0xff, 0xff, 0xfe, 0x7f, 0xff, 0xff, 0xfe, 0x7f, 0xff, 0xff, 0xfe, 0x7f, 0xff, 0xff, 0xfe, 0x3f, 0xff, 0xff, 0xfc, 0x3f, 0xff, 0xff, 0xfc, 0x1f, 0xff, 0xff, 0xf8, 0x07, 0xff, 0xff, 0xe0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00])
icon_light = bytearray([0x00, 0x07, 0xe0, 0x00, 0x00, 0x1f, 0xf8, 0x00, 0x00, 0x3f, 0xfc, 0x00, 0x00, 0x7f, 0xfe, 0x00, 0x00, 0xff, 0xff, 0x00, 0x00, 0xff, 0xff, 0x00, 0x00, 0xff, 0xff, 0x00, 0x00, 0xff, 0xff, 0x00, 0x00, 0x7f, 0xfe, 0x00, 0x00, 0x3f, 0xfc, 0x00, 0x00, 0x1f, 0xf8, 0x00, 0x00, 0x1f, 0xf8, 0x00, 0x00, 0x1f, 0xf8, 0x00, 0x00, 0x1f, 0xf8, 0x00, 0x00, 0x0f, 0xf0, 0x00, 0x00, 0x0f, 0xf0, 0x00, 0x00, 0x0f, 0xf0, 0x00, 0x00, 0x0f, 0xf0, 0x00, 0x00, 0x0f, 0xf0, 0x00, 0x00, 0x07, 0xe0, 0x00, 0x00, 0x07, 0xe0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00])
icon_settings = bytearray([0x00, 0x18, 0x18, 0x00, 0x00, 0x3c, 0x3c, 0x00, 0x0c, 0x3c, 0x3c, 0x30, 0x0f, 0xff, 0xff, 0xf0, 0x07, 0xff, 0xff, 0xe0, 0x03, 0xfc, 0x3f, 0xc0, 0x1b, 0xf0, 0x0f, 0xd8, 0x1b, 0xf0, 0x0f, 0xd8, 0x3d, 0xe0, 0x07, 0xbc, 0x3d, 0xe0, 0x07, 0xbc, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x3d, 0xe0, 0x07, 0xbc, 0x3d, 0xe0, 0x07, 0xbc, 0x1b, 0xf0, 0x0f, 0xd8, 0x1b, 0xf0, 0x0f, 0xd8, 0x03, 0xfc, 0x3f, 0xc0, 0x07, 0xff, 0xff, 0xe0, 0x0f, 0xff, 0xff, 0xf0, 0x0c, 0x3c, 0x3c, 0x30, 0x00, 0x3c, 0x3c, 0x00, 0x00, 0x18, 0x18, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00])

# --- 5. Seri Haberleşme Motoru (C# WinForms İletişimi) ---
def komut_isle(komut):
    global su_anki_dosya, aktif_profil, needs_update
    if komut == "WHO_ARE_YOU":
        print("MACRO_PAD")
    elif komut == "GET_PROFILES":
        try:
            files = [f for f in os.listdir("/") if f.endswith(".json") and f != "settings.json"]
            aktif = su_anki_dosya if su_anki_dosya else (files[0] if files else "")
            print("PROFILE_LIST:" + ",".join(files) + "|ACTIVE:" + aktif)
        except:
            print("PROFILE_LIST:|ACTIVE:")
    elif komut.startswith("GET_PROFILE:"):
        dosya = komut[12:].strip()
        try:
            with open("/" + dosya, "r") as f:
                p_data = json.load(f)
                print("PROFILE_DATA:" + json.dumps(p_data))
        except Exception as e:
            print("PROFILE_DATA:ERROR")
    elif komut.startswith("SET_PROFILE:"):
        dosya = komut[12:].strip()
        profil_yukle(dosya)
        needs_update = True
    elif komut.startswith("SAVE_PROFILE:"):
        veri = komut[13:]
        if "|" in veri:
            dosya_adi, icerik = veri.split("|", 1)
            dosya_adi = dosya_adi.strip()
            try:
                p_data = json.loads(icerik)
                with open("/" + dosya_adi, "w") as f:
                    json.dump(p_data, f)
                profil_yukle(dosya_adi)
                print("SAVE_SUCCESS")
                needs_update = True
            except Exception as e:
                print("SAVE_ERROR")
        else:
            print("SAVE_ERROR")
    elif komut.startswith("DELETE_PROFILE:"):
        dosya = komut[15:].strip()
        try:
            os.remove("/" + dosya)
            files = [f for f in os.listdir("/") if f.endswith(".json") and f != "settings.json"]
            if su_anki_dosya == dosya:
                if files:
                    profil_yukle(files[0])
                else:
                    aktif_profil = {"isim": "Varsayilan", "tuslar": [{"gorev": "YOK"}] * 10}
                    su_anki_dosya = ""
            print("DELETE_SUCCESS")
            needs_update = True
        except:
            print("DELETE_ERROR")
    elif komut.startswith("RENAME_PROFILE:"):
        veri = komut[15:].strip()
        if "|" in veri:
            eski_ad, yeni_ad = veri.split("|", 1)
            eski_ad = eski_ad.strip()
            yeni_ad = yeni_ad.strip()
            try:
                os.rename("/" + eski_ad, "/" + yeni_ad)
                if su_anki_dosya == eski_ad:
                    su_anki_dosya = yeni_ad
                    ayarlari_kaydet()
                print("RENAME_SUCCESS")
                needs_update = True
            except:
                print("RENAME_ERROR")

def seri_port_kontrol():
    global seri_buffer
    while supervisor.runtime.serial_bytes_available:
        try:
            c = sys.stdin.read(1)
            if not c:
                break
            if c == '\n' or c == '\r':
                if seri_buffer:
                    komut = seri_buffer.strip()
                    seri_buffer = ""
                    komut_isle(komut)
            else:
                seri_buffer += c
        except:
            seri_buffer = ""
            break

def acilis_animasyonu():
    oled.fill(0)
    for i in range(0, 32, 4):
        seri_port_kontrol()
        oled.rect(64-i*2, 32-i, i*4, i*2, 1)
        oled.show()
    oled.fill(0)
    oled.text("FELIX AI", 40, 15, 1)
    oled.text("MacroPad", 40, 30, 1)
    oled.text("v4.0 Ready", 35, 45, 1)
    oled.show()
    seri_port_kontrol()
    time.sleep(0.2)
    if isik_durumu:
        ana_renk = renkler[isik_modu_indeks]
        birikenler = []
        for i in range(len(tus_led_sirasi)-1, -1, -1):
            for j in range(i + 1):
                seri_port_kontrol()
                pixels.fill((0,0,0))
                for b in birikenler: pixels[b] = ana_renk
                pixels[tus_led_sirasi[j]] = ana_renk
                pixels.show()
                time.sleep(0.01)
            birikenler.append(tus_led_sirasi[i])
    led_durumu_ayarla()

# --- 6. Başlatma ---
try:
    with open("/settings.json", "r") as f:
        k = json.load(f)
        ekran_parlaklik, led_parlaklik = k.get("ekran_p", 100), k.get("led_p", 50)
        isik_durumu, isik_modu_indeks = k.get("isik_d", True), k.get("isik_m", 0)
        uyku_modu_aktif, macropad_yazisi = k.get("uyku_a", True), k.get("yazi_g", True)
        su_anki_dosya = k.get("son_p", "")
except: pass

oled.contrast(int(ekran_parlaklik * 2.55))
acilis_animasyonu()
ayarlar_listesi = ["Ekran Parlak", "Uyku Modu", "Yazi Goster", "GERI"]
isik_listesi = ["Durum", "Renk", "LED Parlak", "GERI"]
json_dosyalari = [f for f in os.listdir("/") if f.endswith(".json") and f != "settings.json"]
if su_anki_dosya in json_dosyalari: profil_yukle(su_anki_dosya)
elif json_dosyalari: profil_yukle(json_dosyalari[0])

# --- 7. Ana Döngü ---
while True:
    seri_port_kontrol()
    now = time.monotonic()
    
    # 1. YÖNETİM MODU GİRİŞ/ÇIKIŞ (GP0+GP1+GP2)
    if not tuslar[0].value and not tuslar[1].value and not tuslar[2].value:
        yonetim_modu = not yonetim_modu
        mod = 0; needs_update = True
        while not tuslar[0].value or not tuslar[1].value or not tuslar[2].value:
            if time.monotonic() - last_blink > 0.4:
                blink_state = not blink_state; last_blink = time.monotonic(); led_durumu_ayarla()
        ayarlari_kaydet()
        tus_durumlari[0], tus_durumlari[1], tus_durumlari[2] = True, True, True 
        continue

    # 2. ORTAK NAVİGASYON (Encoder)
    diff = 0
    if encoder:
        curr_pos = encoder.position
        if curr_pos != last_encoder_pos:
            diff = curr_pos - last_encoder_pos
            last_encoder_pos = curr_pos

    # 3. YÖNETİM MODU TUŞLARI (Sol/Sağ)
    if yonetim_modu:
        if not tuslar[0].value and tus_durumlari[0]: diff = -1; tus_durumlari[0] = False
        elif tuslar[0].value: tus_durumlari[0] = True
        
        if not tuslar[1].value and tus_durumlari[1]: diff = 1; tus_durumlari[1] = False
        elif tuslar[1].value: tus_durumlari[1] = True

    # Navigasyon Kaydırma İşlemi
    if diff != 0:
        last_interaction = now
        if uyku_durumu: uyku_durumu = False; needs_update = True
        else:
            if mod == 0: secili_indeks = (secili_indeks + diff) % 3
            elif mod == 1: 
                limit = len(json_dosyalari)+1 if mod==3 else 4
                liste_indeks = (liste_indeks + diff) % limit
            elif mod == 2:
                if secili_indeks == 2:
                    if liste_indeks == 0: 
                        ekran_parlaklik = max(0, min(100, ekran_parlaklik + diff*10))
                        oled.contrast(int(ekran_parlaklik * 2.55))
                    elif liste_indeks == 1: uyku_modu_aktif = not uyku_modu_aktif
                    elif liste_indeks == 2: macropad_yazisi = not macropad_yazisi
                elif secili_indeks == 1:
                    if liste_indeks == 1: isik_modu_indeks = (isik_modu_indeks + diff) % 4
                    elif liste_indeks == 2: led_parlaklik = max(0, min(100, led_parlaklik + diff*10))
                    led_durumu_ayarla()
            elif mod == 3:
                files = [f for f in os.listdir("/") if f.endswith(".json") and f != "settings.json"]
                liste_indeks = (liste_indeks + diff) % (len(files) + 1)
            needs_update = True

    # 4. ONAY (OK) KONTROLÜ (Encoder Butonu veya GP2)
    ok_basildi = False
    if not enc_button.value and enc_button_prev: ok_basildi = True
    enc_button_prev = enc_button.value

    if yonetim_modu and not tuslar[2].value and tus_durumlari[2]:
        ok_basildi = True
        tus_durumlari[2] = False
    elif tuslar[2].value:
        tus_durumlari[2] = True

    if ok_basildi:
        last_interaction = now
        if uyku_durumu: uyku_durumu = False; needs_update = True
        else:
            if mod == 0:
                if secili_indeks == 0: mod = 3; liste_indeks = 0
                else: mod = 1; liste_indeks = 0
            elif mod == 1:
                su_l = isik_listesi if secili_indeks == 1 else ayarlar_listesi
                if su_l[liste_indeks] == "GERI": mod = 0
                elif su_l[liste_indeks] == "Durum": isik_durumu = not isik_durumu; led_durumu_ayarla()
                else: mod = 2
            elif mod == 2: mod = 1; ayarlari_kaydet()
            elif mod == 3:
                files = [f for f in os.listdir("/") if f.endswith(".json") and f != "settings.json"]
                if liste_indeks < len(files): profil_yukle(files[liste_indeks]); mod = 0; yonetim_modu = False
                else: mod = 0
            needs_update = True

    # 5. NORMAL MAKRO TUŞLARI (Sadece Yönetim Modu Kapalıyken)
    if not yonetim_modu:
        for i in range(10):
            val = tuslar[i].value
            if val != tus_durumlari[i]:
                tus_durumlari[i] = val
                if not val: # Tuşa basıldı
                    last_interaction = now
                    if uyku_durumu: uyku_durumu = False; needs_update = True; continue
                    
                    if i < len(aktif_profil['tuslar']):
                        gorev = aktif_profil['tuslar'][i]['gorev']
                        print(f"MAKRO TETIKLENDI: {gorev}")
                        makro_calistir(gorev)

    # 6. ZAMANLAYICILAR
    if now - last_blink > 0.4:
        blink_state = not blink_state; last_blink = now; led_durumu_ayarla()
        if mod == 2: needs_update = True
    if uyku_modu_aktif and now - last_interaction > 30:
        if not uyku_durumu: uyku_durumu = True; needs_update = True

    # 7. EKRAN GÜNCELLEME
    if needs_update:
        oled.fill(0)
        if uyku_durumu:
            oled.contrast(5)
            if macropad_yazisi: oled.text("MACROPAD", 40, 28, 1)
        else:
            oled.contrast(255)
            ana_basliklar = ["PROFILLER", "ISIKLAR", "AYARLAR", "PROFIL SEC"]
            b_txt = ana_basliklar[mod if mod == 3 else secili_indeks]
            oled.fill_rect(0, 0, 128, 16, 1); oled.text(b_txt, (128-len(b_txt)*6)//2, 4, 0); oled.line(0, 17, 128, 17, 1)
            
            if mod == 0:
                ikonlar = [icon_profile, icon_light, icon_settings]; ikon_ciz(ikonlar[secili_indeks], 48, 25)
            elif mod in [1, 2]:
                l = isik_listesi if secili_indeks == 1 else ayarlar_listesi
                for idx, item in enumerate(l):
                    y = 22 + (idx * 10)
                    if idx == liste_indeks:
                        if mod == 2: oled.fill_rect(0, y-1, 128, 10, 1); col=0
                        else: oled.rect(0, y-1, 128, 10, 1); col=1
                    else: col=1
                    val = ""
                    if secili_indeks == 2:
                        if idx == 0: val = f": %{ekran_parlaklik}"
                        elif idx == 1: val = ": ON" if uyku_modu_aktif else ": OFF"
                        elif idx == 2: val = ": ON" if macropad_yazisi else ": OFF"
                    elif secili_indeks == 1:
                        if idx == 0: val = ": ON" if isik_durumu else ": OFF"
                        elif idx == 1: val = f": {efektler[isik_modu_indeks]}"
                        elif idx == 2: val = f": %{led_parlaklik}"
                    oled.text(item + val, 5, y, col)
            elif mod == 3:
                files = [f for f in os.listdir("/") if f.endswith(".json") and f != "settings.json"]
                files.append("GERI")
                for idx, f in enumerate(files):
                    y = 22 + (idx * 10); txt = f.replace(".json", "")
                    if idx == liste_indeks: oled.fill_rect(0, y-1, 128, 10, 1); col=0
                    else: col=1
                    oled.text(txt, 5, y, col)
        oled.show(); needs_update = False