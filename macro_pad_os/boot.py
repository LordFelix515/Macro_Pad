import board
import digitalio
import storage

# 1. Tuşun (GP0) donanımsal kurulumu
tus1 = digitalio.DigitalInOut(board.GP0)
tus1.direction = digitalio.Direction.INPUT
tus1.pull = digitalio.Pull.UP

# tus1.value True ise tuşa BASILMIYOR (Boşta) demektir
if tus1.value:
    # 1. Cihazın bilgisayarda "USB Sürücüsü (Disk)" olarak görünmesini tamamen kapat.
    storage.disable_usb_drive()
    
    # 2. Disk PC'ye kapalı olduğu için, Pico'nun (code.py'nin) 
    # settings.json dosyasına ayarları özgürce kaydedebilmesi için yazma iznini cihaza ver.
    try:
        storage.remount("/", readonly=False)
    except:
        pass
else:
    # Eğer kabloyu takarken 1. tuşa (GP0) BASILI TUTULUYORSA bu bloğa girer.
    # storage.disable_usb_drive() ÇALIŞMAZ, yani disk bilgisayarda GÖRÜNÜR.
    # Profil (.json) eklemek veya kod düzenlemek için bu mod kullanılır.
    pass