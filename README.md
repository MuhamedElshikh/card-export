# قارئ البطاقة القومية المصرية — Egyptian National ID Card Reader

تطبيق يعمل **بالكامل بدون إنترنت**: يقرأ صورة البطاقة على جهازك بعد تجهيزها (تصحيح الاتجاه
والتباين والميل والتباين الثنائي)، باستخدام Tesseract مع موديلين عربي وإنجليزي مرفقين مع البرنامج،
يستخرج الرقم القومي والاسم والعنوان، يتحقق من الرقم قواعديًا، يعرض البيانات للمراجعة والتصحيح، ثم
يحفظها في قاعدة بياناتك المحلية.

> **الخصوصية أولًا:** لا تُرسل الصور إلى أي خدمة سحابية، ولا يوجد أي اتصال شبكي وقت التشغيل،
> ولا تُحفظ الصور افتراضيًا، والسجلات لا تحتوي أسماء أو أرقامًا قومية أو بايتات صور.

---

## 1. المتطلبات

| المطلوب | النسخة |
| --- | --- |
| .NET SDK | 8.0 أو أحدث (المشاريع تستهدف `net8.0`) |
| SQL Server | LocalDB على ويندوز، أو حاوية SQL Server (Docker) على لينكس |
| Node.js + npm | Node 20+ (تم الاختبار على 24) — للواجهة فقط |
| Docker | اختياري، لتشغيل كل شيء معًا |

لا حاجة لتثبيت Tesseract على الجهاز: حزمة `Tesseract` والموديلان `tessdata/ara.traineddata`
و`tessdata/eng.traineddata` مضمّنان داخل المستودع وينسخان تلقائيًا إلى مجلد الإخراج. مكتبة
SkiaSharp (MIT) تجهّز الصورة محليًا قبل OCR: تصحيح EXIF، تصغير، تدرّج رمادي، تمديد تباين،
تباين ثنائي محلي/عالمي، وإزالة الميل البسيط.

---

## 2. التشغيل السريع (ويندوز + LocalDB)

```powershell
# 1) استعادة أدوات EF المحلية (dotnet-ef 8.0.8)
dotnet tool restore

# 2) إنشاء قاعدة البيانات وتطبيق الهجرة
dotnet ef database update --project Infrastructure --startup-project Api

# 3) تشغيل الـ API
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run --project Api --urls http://127.0.0.1:5099
# Swagger: http://127.0.0.1:5099/swagger

# 4) تشغيل الواجهة (نافذة أخرى)
cd web
npm install
npm start          # http://localhost:4200
```

الواجهة تستخدم `proxy.conf.json` لتمرير `/api` إلى `http://127.0.0.1:5099`، لذلك لا تحتاج CORS
أثناء التطوير. إن أردت الاتصال بخادم آخر، غيّر `target` في `web/proxy.conf.json`.

### تشغيل بدون SQL Server (وضع التطوير/الاختبار فقط)

```powershell
$env:Database__Provider='Sqlite'
$env:ConnectionStrings__IdCardReaderDb='Data Source=./idcardreader-dev.db'
dotnet run --project Api
```

في هذا الوضع يُبنى المخطط من الموديل (`EnsureCreated`) بدلًا من الهجرات، لأن الهجرات مكتوبة لـ SQL Server.

---

## 3. Docker (كل شيء معًا)

```bash
cp .env.example .env      # ثم ضع كلمة سر قوية لـ SQL_SA_PASSWORD
docker compose up --build
# الواجهة: http://localhost:4200
# الـ API : http://localhost:5099/swagger  (في بيئة Development فقط)
```

- `web` = Nginx يخدم ملفات Angular ويعمل كـ reverse proxy لـ `/api` (نفس الأصل، بدون CORS).
- `api` = ASP.NET Core 8 على منفذ 8080 داخليًا، يعمل بمستخدم غير جذري.
- `sqlserver` = SQL Server 2022 Express مع healthcheck وفوليوم دائم.

> ملاحظة: Docker غير مثبّت على جهاز التطوير الحالي، لذلك مسار الحاويات مكتوب ومُراجع لكنه لم يُشغَّل
> محليًا. مسار LocalDB أعلاه هو المُتحقَّق منه فعليًا.

---

## 4. الإعدادات (`Api/appsettings.json`)

| المفتاح | الافتراضي | المعنى |
| --- | --- | --- |
| `ConnectionStrings:IdCardReaderDb` | LocalDB | اتصال SQL Server |
| `Database:Provider` | `SqlServer` | `Sqlite` للتطوير/الاختبار فقط |
| `Ocr:MaxConcurrentScans` | `1` | حد التزامن (اتركه 1 على الأجهزة الضعيفة) |
| `Ocr:TessdataPath` | مجلد `tessdata` بجوار الملف التنفيذي | مسار موديلات اللغة |
| `Ocr:NumberLongEdge` | `1100` | أكبر ضلع لصورة قراءة الرقم (أصغر من هذا يُكبَّر إلى 900 كحد أدنى) |
| `Ocr:TextLongEdge` | `1800` | أكبر ضلع لصورة قراءة الاسم والعنوان |
| `Ocr:EnablePreprocessing` | `true` | تجهيز الصورة قبل OCR (أطفئه للتشخيص فقط) |
| `Ocr:EnableDeskew` | `true` | تقدير وإزالة ميل النص |
| `Storage:StoreImage` | `false` | حفظ الصور (مُعطَّل افتراضيًا) |
| `Storage:Path` | `App_Data/idcards` | مكان الملفات المشفّرة (خارج `wwwroot`) |
| `Storage:EncryptionKey` | فارغ | مفتاح AES-256 بصيغة base64 (32 بايت) — إلزامي عند تفعيل الحفظ |
| `RateLimiting:Scan:PermitLimit` | `10` | عدد طلبات المسح لكل IP في الدقيقة |
| `Cors:AllowedOrigins` | `http://localhost:4200` | مطلوب فقط عند الاتصال المباشر بدون proxy |

توليد مفتاح تشفير:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

---

## 5. واجهة الـ API

| الطريقة | المسار | الوظيفة |
| --- | --- | --- |
| `POST` | `/api/idcards/scan` | multipart باسم `image` (JPG/PNG حتى 5MB) → معاينة، **لا يحفظ شيئًا** |
| `POST` | `/api/idcards` | حفظ بطاقة بعد المراجعة (يعيد التحقق من الرقم على الخادم) |
| `GET` | `/api/idcards?search=&page=1&pageSize=20` | قائمة مُصفّحة بالبحث |
| `GET` | `/api/idcards/{id}` | بطاقة واحدة |
| `GET` | `/health` | فحص حياة الخدمة |

الأخطاء بصيغة `application/problem+json` مع حقل `code` قابل للترجمة:
`invalid_national_id`, `duplicate_national_id`, `validation_failed`, `image_required`,
`image_too_large`, `unsupported_image_format`, `invalid_image`, `image_storage_disabled`,
`scan_failed`, `id_card_not_found`.

قواعد الرقم القومي المطبَّقة في الخادم والواجهة معًا:

- تحويل الأرقام العربية-الهندية (٠-٩) والفارسية (۰-۹) إلى ASCII.
- 14 رقمًا بالضبط.
- الخانة 1: القرن (2 = 1900s، 3 = 2000s).
- الخانات 2-7: `YYMMDD` بتاريخ حقيقي غير مستقبلي.
- الخانتان 8-9: كود المحافظة (01-04، 11-19، 21-29، 31-35، 88).
- الخانة 13: فردي = ذكر، زوجي = أنثى.
- لا يوجد checksum على الخانة 14.

---

## 6. الاختبارات

```powershell
dotnet test                          # 78 اختبارًا (Domain + Application + Infrastructure + API)
cd web; npx ng test --watch=false     # 23 اختبارًا (الواجهة)
```

| المشروع | يغطي |
| --- | --- |
| `Tests/Application.Tests` | تحليل الرقم القومي، اشتقاق الحقول، خدمة الحفظ (صالح/مكرر/غير صالح/صورة معطّلة) |
| `Tests/Infrastructure.IntegrationTests` | الفهرس الفريد فعليًا على قاعدة علائقية، قواعد اختيار الرقم والتصويت، تجهيز الصور، ومحرك Tesseract حقيقي على عيّنات خطوط مُولّدة |
| `Tests/Api.Tests` | عقد HTTP كامل عبر `WebApplicationFactory`: 201/400/404/409، رفض الملفات غير الصورية، حدود الحجم |

---

## 7. قياس الدقة والسرعة والذاكرة

الأرقام الحقيقية **لا تُشتق من الخيال**: شغّل أداة القياس على صورك.

```powershell
dotnet run --project Tools/IdCards.Eval -- samples/id-cards samples/ground-truth.csv
```

`ground-truth.csv` (اختياري لكنه لازم لحساب الدقة):

```csv
fileName,nationalId,fullName,address
card-01.jpg,29001011234567,أحمد محمد علي,"القاهرة، مدينة نصر"
```

تُطبع: نسبة استخراج الرقم، مطابقة الرقم/الاسم/العنوان بالضبط، نسبة `lowConfidence`، متوسط وزمن
p95 والحد الأقصى، وذروة الذاكرة (peak working set) مع الأساس.

> الصور والأسماء الحقيقية **لا تُرفع للمستودع**: `samples/` و`test-images/` و`*.db` و`App_Data/`
> مُستثناة في `.gitignore` من أول commit.

---

## 8. ما هو مُنفَّذ وما هو غير مُنفَّذ (بصراحة)

**مُنفَّذ ومُتحقَّق منه:**

- تحليل الرقم القومي بكل قواعده + خريطة المحافظات (28 كودًا).
- OCR offline بمرحلتين: تجهيز الصورة (SkiaSharp)، ثم قراءة الرقم بأربع زوايا محتملة بموديلَي
  `eng` و`ara` مع إيقاف مبكر عند اتفاق مرورين. **لا يُخترع رقم**: يُقبل فقط سطر فيه 14 رقمًا
  بالضبط، ولا تُستخدم نوافذ داخل أرقام أخرى ولا أرقام ملصقة من سطور مختلفة. `lowConfidence` عند
  تصويت أقل من 60% أو قراءة واحدة فقط. الاسم والعنوان يُقرآن من السطور المُعنونة فقط، مع رفض
  `بالكامل` وعناوين البطاقة؛ الحقل الفاضي أفضل من البيانات الغلط. الميلاد والنوع والمحافظة
  مشتقة من الرقم القومي المقبول فقط، وليست مطلوبة صورة الظهر.
- API كامل + SQL Server + هجرة حقيقية + فهرس فريد + Rate limiting + ProblemDetails.
- واجهة عربية/إنجليزية RTL/LTR بـ signals وOnPush وngx-translate، مع مراجعة وتصحيح وحفظ وقائمة وبحث.
- تخزين صور اختياري مشفّر بـ AES-256-GCM خارج `wwwroot`.

**غير مُنفَّذ (بأمانة):**

- **كشف حدود البطاقة وتصحيح المنظور** (card contour + perspective warp) و**قصّ الحقول من ملف
  تخطيط JSON**: لا يوجد ربط OpenCV صالح لـ `net8.0` على NuGet (الحزمة القديمة `OpenCvSharp`
  2.4.0.1 تستهدف .NET Framework فقط). لذلك تُقرأ الصورة كاملة، حتى بعد تجهيزها.
  إن كانت صورك تحتوي البطاقة في جزء صغير من الإطار، قصّها حول البطاقة قبل الرفع أو أرسل لي
  صورة حقيقية لأضبط عليها المسار.
- **أرقام دقة/زمن/ذاكرة حقيقية**: تحتاج صورك عبر أداة القياس أعلاه.
- **AutoMapper**: استُبدل بمُحوِّل صريح لأن الإصدار 13.0.1 عليه ثلاث ثغرات عالية الخطورة
  والإصدار 15+ تجاري الترخيص. التفاصيل في `docs/architecture.md`.
- **Ardalis.Specification**: لم تُضف لأن سطح الاستعلام (قراءة واحدة مفلترة ومُصفّحة) لا يبرر طبقة إضافية.

---

## 9. هيكل المشروع

```
Domain/          قواعد الرقم القومي والكيان (بدون أي تبعية)
Application/     العقود (ports)، خدمة الحفظ، التحقق، DTOs
Infrastructure/  EF Core + SQL Server، Tesseract OCR، مخزن الصور المشفّر
Api/             Controllers، Rate limiting، ProblemDetails، Swagger
Tools/IdCards.Eval  أداة قياس الدقة والزمن والذاكرة
Tests/           اختبارات الوحدة والتكامل وعقد HTTP
web/             Angular standalone + signals + ngx-translate
docs/            architecture.md، task_board.md، error_log.md، environment_capabilities.md
```
