# คู่มือโครงสร้างและการทำงานของ EpicCottonGame

ไฟล์นี้อธิบายโครงสร้างภายในของโครงงานสำหรับการศึกษาและการนำเสนอ โดยเน้นว่าไฟล์ใดทำหน้าที่อะไร และข้อมูลไหลผ่านระบบอย่างไร

## ภาพรวมการทำงาน

ลำดับหลักของเกมคือ

`Market → Inventory → Use → Target → Confirm → World Action → Save`

สำหรับการปลูก:

`ซื้อเมล็ด → เข้า Inventory → เลือกเมล็ด → กด USE → เลือกพิกัดว่าง → กด Space → สร้างต้นฝ้ายถาวร`

สำหรับการเก็บ:

`ต้นฝ้ายโต → ฝ้ายพร้อมเก็บ → กดค้างและลากฝ้าย → วางลง Bag → ได้ Cotton → ต้นเดิมเริ่มรอบปลูกใหม่`

สำหรับปุ๋ย:

`ซื้อปุ๋ย → Inventory → เลือกปุ๋ย → USE → เลือกต้นที่กำลังโต → Confirm → ปุ๋ยมีผลต่อฝ้ายรอบถัดไป`

สำหรับการรักษา:

`เปิด Combined Shop → กด Medic ด้านล่าง → FULL HEAL? → YES — HEAL → จ่ายเงิน → HP กลับเต็ม`

ราคา Medic คำนวณจาก `Max HP + 25%` และไม่สามารถซื้อได้ถ้า HP เต็มหรือเงินไม่พอ

## โครงสร้างไฟล์

### `EpicCottonGame.csproj`

กำหนดว่าโครงการเป็น **Blazor WebAssembly บน .NET 8** และลงแพ็กเกจที่จำเป็นสำหรับการทำงานบนเบราว์เซอร์

### `Program.cs`

เป็นจุดเริ่มต้นของโปรแกรม ทำหน้าที่สร้าง WebAssembly host และลงทะเบียน service หลัก ได้แก่ `GameEngine` และ `LocalSaveService`

### `App.razor`

เป็น root component ของแอป และโหลดหน้าเกมหลักจาก `Pages/Game.razor`

### `_Imports.razor`

รวม namespace ที่ใช้ร่วมกันใน Razor เช่น Models และ Services เพื่อลดการเขียน `@using` ซ้ำหลายไฟล์

### `Models/GameModels.cs`

เก็บโครงสร้างข้อมูลหลัก เช่น

- `CottonType` — ประเภทและค่าพื้นฐานของฝ้าย
- `MutationConfig` — ข้อมูล Mutation
- `CottonInstance` — ฝ้ายแต่ละชิ้นที่ผู้เล่นเป็นเจ้าของ
- `CottonStem` — ต้นฝ้ายที่อยู่ในโลก
- `ShopItemConfig` — ค่าของอุปกรณ์ Glove/Bag
- `MarketItemConfig` — สินค้าที่ขายใน Global Market

### `Pages/Game.razor`

เป็นทั้งชั้นแสดงผลและชั้นรับ input ของผู้เล่น ได้แก่

- วาดโลกและพิกัดต้นฝ้ายที่มองเห็น
- แสดง Shop, Bag และ Hearts
- รับการลากฝ้ายไป Bag
- รับการลากกล้องเพื่อเลื่อนแผนที่
- รับการซูมด้วยเมาส์
- เปิด Market และ Inventory
- จัดการการเลือก Item และ Target
- จัดการ Confirmation และปุ่มลัด `Space` / `Escape`

ไฟล์นี้ไม่ควรเป็นเจ้าของกฎเศรษฐกิจหลักของเกม เพราะกฎเหล่านั้นอยู่ใน `GameEngine`

### `Services/GameEngine.cs`

เป็นแกนกลางของระบบเกมและถือ state จริงของเกมปัจจุบัน เช่น

- เงิน
- HP
- Glove Tier
- Bag Tier
- Inventory
- Cotton ใน Bag
- Market Stock
- รายการ Cotton ใน Market
- ต้นฝ้ายทั้งหมดในโลก
- Mutation/Fertilizer modifiers

โลกไม่ได้ใช้ Array ขนาดตายตัว แต่เก็บต้นฝ้ายด้วยพิกัด `(X,Y)` ใน `Dictionary<(int X, int Y), CottonStem>` เพื่อให้ขยายพื้นที่ปลูกได้ไม่จำกัดตามการใช้งานจริง

### `Services/GameEngineSaveModels.cs`

แปลง state ของเกมเป็นรูปแบบที่บันทึกลง browser ได้ เช่น เงินในรูปข้อความ `BigInteger`, รายการต้นฝ้าย, Inventory และ Cotton ที่ถืออยู่

### `Services/LocalSaveService.cs`

จัดการบัญชีผู้เล่นแบบ local และ autosave ด้วย `localStorage`

ระบบแบ่งเป็น

`Account ID → Save Envelope → Signature → GameSaveState`

มี version ของ save และตรวจสอบความถูกต้องก่อน restore เพื่อกันการแก้ save แบบง่าย ๆ สำหรับการสาธิตในห้องเรียน

ระบบนี้ไม่ใช่ระบบรักษาความปลอดภัยสำหรับ multiplayer จริง เพราะ browser เป็นของผู้ใช้เอง

### `Services/MoneyFormatter.cs`

แสดงเงิน `BigInteger` เป็นรูปแบบอ่านง่าย เช่น

`1,000 → 1K`

`1,000,000 → 1M`

`1,000,000,000 → 1B`

และรองรับ suffix ที่สูงขึ้นโดยไม่แปลงค่าหลักเป็น `double`

### `Services/BigIntegerJsonConverter.cs`

ทำให้ `BigInteger` สามารถอ่าน/เขียนผ่าน JSON configuration และ save data ได้

## ไฟล์ Configuration

### `wwwroot/assets/cotton/*.json`

กำหนดประเภทฝ้าย เช่น Common, Rare, Epic และ Golden รวมถึงโอกาส, ความเสียหาย, มูลค่า และรูปภาพ

### `wwwroot/assets/mutations.json`

กำหนด Mutation เช่น Frozen, Burning และ Crystallized รวมถึงโอกาสและตัวคูณมูลค่า

### `wwwroot/assets/shop.json`

กำหนดฐานราคาและผลของ Glove/Bag โดย tier ของอุปกรณ์ไม่มีจุดสิ้นสุดตายตัว

### `wwwroot/assets/globalmarket.json` — Global Market มีเมล็ดฝ้ายเพียงชนิดเดียว (Cotton Seed) และ fertilizer

กำหนดสินค้าที่ซื้อจาก Global Market โดยมีเมล็ดเพียงชนิดเดียวคือ `Cotton Seed` และมี fertilizer หลายชนิด รวมถึงราคา, stock, และผลของ fertilizer ต่อ rarity/mutation ของการเก็บครั้งถัดไป

## วงจรของต้นฝ้าย

ต้นใหม่เริ่มที่ `Growing`

`Growing → Ready → Harvest → Growing`

เมื่อถึงเวลาพร้อมเก็บ `GameEngine.Tick()` จะสุ่ม

1. Cotton rarity
2. Mutation
3. state ใหม่เป็น `Ready`

เมื่อผู้เล่นลากฝ้ายไปวางใน Bag จะเรียก `Harvest(x,y)`

`Harvest()` จะ

- ตรวจว่าต้นยังพร้อมเก็บ
- ตรวจว่า Bag ยังไม่เต็ม
- สร้าง `CottonInstance`
- คำนวณความเสียหาย
- เพิ่มฝ้ายเข้า Bag
- ทำให้ต้นเดิมกลับไป `Growing`
- ล้าง fertilizer modifier ของรอบที่ผ่านมา

ดังนั้นต้นเดิมจะไม่หายไปหลังเก็บเกี่ยว

## การทำงานของ Fertilizer

Fertilizer ไม่ได้มีหน้าที่หลักในการเร่งเวลาโต

มันเพิ่ม modifier ให้ต้นฝ้าย เช่น

- `RarityMultiplier`
- `MutationMultiplier`
- `MutationChanceBonus`

Modifier จะอยู่กับต้นจนถึงการสร้าง Cotton ในรอบถัดไป จากนั้นจะถูกล้างเมื่อเริ่มรอบใหม่

## การทำงานของ Glove

Glove มี tier ต่อเนื่องและไม่มี max tier ที่กำหนดตายตัว

ราคาถูกคำนวณจาก `BaseCost`, tier และ `CostMultiplier`

ผลของ Glove ประกอบด้วย

- ลดความเสียหายตอนเก็บแบบเปอร์เซ็นต์ 3% ต่อต่อเทียร์ สูงสุด 90%
- เพิ่ม Max HP
- เปลี่ยนรูป glove
- เปลี่ยนรูป cursor ตาม tier

## การทำงานของ Bag

Bag มี tier ต่อเนื่องเช่นเดียวกับ Glove

ผลของ Bag ประกอบด้วย

- เพิ่มความจุ Cotton
- เพิ่ม bonus ตอนขาย
- เปลี่ยนรูป Bag ตาม tier

Bag เป็นวัตถุที่มองเห็นในโลกและรองรับการลากฝ้ายมาวางลงใน Bag โดยตรง

## การลาก Cotton

ระบบลากใช้ event ของเมาส์ใน `Game.razor`

`MouseDown บน Cotton → draggingCotton = stem → MouseMove → Cotton ตาม cursor → MouseUp`

เมื่อปล่อยเมาส์ ระบบตรวจว่า cursor อยู่เหนือ Bag หรือไม่

- อยู่เหนือ Bag → `Engine.Harvest(x,y)`
- ไม่อยู่เหนือ Bag → ไม่เก็บ Cotton

นี่เป็น interaction หลักของเกม จึงไม่ควรเปลี่ยนเป็น click-to-harvest แบบธรรมดา

## Medic Shop และการจัดการ HP

Medic อยู่เป็นส่วนถาวรด้านล่างของ Combined Shop โดยใช้ภาพ world object ของ Global Market เป็นจุดเข้า

เมื่อเปิด Combined Shop จะมี Global Market และ Upgrade Shop เป็นแท็บหลัก และมี Medic dock อยู่ด้านล่างเสมอ; การกด Medic จะแสดงคำถาม `FULL HEAL?` พร้อมราคา `Max HP + 25%`

ถ้าเลือก `YES — HEAL` และมีเงินพอ ระบบจะหักเงินและตั้ง `Hp = MaxHp`

## การจัดการเมล็ด

Cotton Seed เป็น inventory item แบบนับจำนวน ไม่ใช่ item แบบ infinite

การปลูกแต่ละครั้งเรียก `Consume("cotton_seed")` และจำนวนเมล็ดลดลงจริง

Global Market มี seed สำหรับปลูกเพียงชนิดเดียว และไม่มี seed rarity/variant แบบอื่น

## การเลื่อนโลก

โลกเป็น infinite coordinate space และมีเส้น grid จาง ๆ เพื่อช่วยให้ผู้เล่นกะระยะการวางต้น

ตัวแปร `CameraX` และ `CameraY` เก็บตำแหน่ง integer grid ที่อยู่กลางหน้าจอ ส่วน `panX` และ `panY` ใช้เก็บการเลื่อนระดับพิกเซลระหว่าง grid

การวาดในแต่ละ frame แสดงเฉพาะช่วงพิกัดที่อยู่ใกล้กล้อง แทนที่จะสร้าง object ทุกตำแหน่งในโลกที่เป็นไปได้

เมื่อกด `Space` ระหว่างเล่นปกติ กล้องจะกลับมาที่พิกัดเริ่มต้น `(0,0)` เพื่อป้องกันผู้เล่นหลงใน infinite world

## การ Save

หลัง action สำคัญ ระบบจะเรียก `SaveAsync()` เช่น

- ซื้อของ
- ใช้เมล็ด
- ใช้ปุ๋ย
- เก็บ Cotton
- ขาย Cotton
- ซื้อ Glove/Bag
- เปลี่ยน Favorite
- Respawn
- Market restock / Growth ที่เปลี่ยน state

เมื่อเปิดเกมใหม่

`Load Config → หา Account ID → โหลด Save → ตรวจ Signature → Restore State`

ถ้า save ไม่ผ่านการตรวจสอบ ระบบจะสร้าง state ที่ถูกต้องใหม่แทน

## ข้อจำกัดของระบบ Local Authentication

บัญชีอยู่ใน browser เดียวกับที่เปิดเกม ดังนั้นผู้เล่นแต่ละ browser จะมีบัญชีของตัวเอง

ผู้ใช้ที่มีสิทธิ์แก้ browser storage หรือ JavaScript โดยตรงยังสามารถดัดแปลงเกมได้ เพราะไม่มี server เป็นผู้ควบคุม state

แนวทางนี้เหมาะกับการนำเสนอและเกมต้นแบบบน GitHub Pages ส่วน multiplayer จริงในอนาคตควรย้ายเงิน, inventory และ ownership ไปให้ server เป็นผู้ตรวจสอบ

## ลำดับการทำงานของแอป

```text
Program.cs
   ↓
Blazor Host
   ↓
Game.razor
   ↓
GameEngine
   ├─ World / Stems
   ├─ Economy
   ├─ Inventory
   ├─ Equipment
   ├─ Market
   └─ Cotton / Mutation logic
   ↓
LocalSaveService
   ↓
Browser localStorage
```

## หลักการออกแบบ

EpicCottonGame ตั้งใจรักษา UI แบบ clean world-integrated

- Shop อยู่ในโลก
- Bag เป็นวัตถุที่ลากได้
- Heart อยู่ใกล้ pointer ตามแนวคิดเดิม
- ไม่มี HUD ขนาดใหญ่บังพื้นที่เล่น
- โลกเป็น flat infinite map
- grid เป็นโครงสร้างสำหรับกันต้นทับกันและช่วยให้ผู้เล่นกะตำแหน่ง ไม่ใช่ขอบเขตฟาร์ม

ดังนั้นเกมจึงควรให้ความรู้สึกเป็นโลกเล็ก ๆ ที่ผู้เล่นกำลังสร้างอาณาจักรฝ้ายของตัวเอง มากกว่าจะเป็นหน้าเว็บสำหรับกรอกข้อมูลฟาร์ม


## Health rules
- Passive regeneration restores 25 HP every 3 seconds, capped at Max HP.
- The first-ever harvest has a one-time safety rule: damage greater than Max HP leaves the player at 1 HP instead of immediately collapsing them.
- ผู้เล่นจะไม่รับเมล็ดฟรีตอนเริ่มเกม จะมีต้นฝ้ายถาวร 1 ต้นเป็นจุดเริ่มต้น และการปลูกต้นใหม่ต้องซื้อ Cotton Seed จาก Global Market ราคาเมล็ดจะคำนวณแบบ BaseCost^OwnedSeedUnits โดย OwnedSeedUnits คือจำนวนต้นที่ปลูกแล้วรวมกับจำนวนเมล็ดที่อยู่ใน Inventory เมล็ดฝ้ายมีขายตลอดเวลาและไม่มีวันหมดสต็อก


### ราคาของเมล็ดฝ้าย
Combined Shop มี Global Market และ Upgrade Shop ส่วน Medic จะอยู่ด้านล่างเสมอ ราคาเมล็ดจะคิดจาก Base Price ยกกำลังจำนวนหน่วยเมล็ดที่ครอบครองอยู่ โดยนับทั้งต้นฝ้ายที่ปลูกบนแผนที่และเมล็ดที่อยู่ใน Inventory ต้นฝ้ายเริ่มต้นถาวรทำให้การซื้อเมล็ดครั้งแรกใช้ราคา Base Price และจะไม่มีเมล็ดฟรีตอนเริ่มเกม

## Current Gameplay Systems

- มีฝ้าย 10 ระดับ: Common, Fine, Premium, Rare, Royal, Epic, Mythic, Celestial, Divine และ Golden
- มี Mutation 10 แบบ และแต่ละแบบมีตัวคูณราคาและความเสียหาย
- ความเสียหายตอนเก็บเกี่ยวจะเพิ่มตามระดับฝ้ายและ Mutation
- ถุงมือแต่ละ Tier เพิ่ม Max HP +25 และลดความเสียหาย +25 ต่อ Tier
