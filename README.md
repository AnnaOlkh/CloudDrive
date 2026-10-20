# CloudDrive (етапи 1-3, система типу 2)

Проєкти: Core (DTO, фільтр/сортування, синхронізація) -> Client (HttpClient-обгортка) -> Server (ASP.NET Core + SQLite) -> Tests.
Desktop (WPF) і Web (Blazor WebAssembly) використовують спільні Core + Client. У Client уже є ручний `SyncService`, який WPF викликатиме з GUI.

## Запуск
    ./setup.sh                                   # sln + restore + test
    dotnet run --project CloudDrive.Server       # Swagger: /swagger

Продакшн: `Jwt__Key` (>=32 байти) задавати змінною середовища; `Cors__Origins__0` - адреса Blazor-клієнта.

## Ключові рішення
- Синхронізація: three-way (local / remote / previous snapshot), порівняння за SHA-256, а не за часом.
  Конфлікт -> не перезаписуємо жодну версію; повертаємо `Plan.Conflicts` для явного вирішення в GUI. Видалення автоматично не синхронізується.
  Manifest оновлюється лише після підтверджених операцій (`SyncOperationResult`). Зберігається поза синхронізованою папкою.
- Сервер: `FileEntry.ContentHash` + `Version` (optimistic concurrency, `PUT ?expectedVersion=`);
  UploadedBy/LastEditedBy - FK на User; унікальність папок через два фільтровані індекси (корінь окремо).
- Шлях у сховищі `{ownerId}/{guid}` визначає лише сервер.
- `GET /api/folders/{id}/snapshot` - рекурсивний список {відносний шлях, хеш} для синхронізації.

## Ручна синхронізація (для майбутнього WPF)

```csharp
var api = new CloudDriveApiClient(new HttpClient { BaseAddress = new Uri("http://localhost:5000/") });
await api.LoginAsync(userName, password);
var remoteFolder = await api.GetRootFolderAsync();
var service = new SyncService(api, new SyncManifestStore());
var result = await service.RunAsync(@"C:\MyDrive", remoteFolder.Id);
// result.Completed, result.Failures, result.Plan.Conflicts
```

Конфлікти зберігаються нерозв’язаними, доки користувач їх не вирішить; автоматичного overwrite немає.
Модель не синхронізує видалення. Реалізація не підтримує одночасний запуск синхронізації
тієї самої пари папок, символічні посилання й автоматичний моніторинг змін.

## WPF Desktop (етап 2)

На Windows з .NET 8 SDK, із кореня `CloudDrive`:

```powershell
dotnet sln add CloudDrive.Desktop\CloudDrive.Desktop.csproj  # якщо ще не додано
dotnet run --project CloudDrive.Server --urls http://localhost:5000
# в іншому терміналі:
dotnet run --project CloudDrive.Desktop
```

Desktop підключається до `http://localhost:5000/` (адресу можна змінити у вікні входу).
Функції: login/register, папки, метадані, сортування й фільтр, видимість колонок, upload (у т.ч. drop файлів), download, delete, preview .c/.jpg і ручна двостороння синхронізація.
Конфлікти синхронізації лише відображаються; автоматичного вирішення чи поширення видалень немає.
WPF запускається виключно у Windows.


## WPF: перевірка етапу 2

- У Visual Studio відкрийте `CloudDrive.sln`. Файл solution необхідний, не видаляйте його.
- Запустіть `CloudDrive.Server` та `CloudDrive.Desktop` (два startup projects).
- З командного рядка: `dotnet test CloudDrive.Tests/CloudDrive.Tests.csproj`.
- Індивідуальну операцію перевіряють `CloudDrive.Tests/Core/FileListQueryTests.cs` (ASC/DESC, All, TxtJpg, TxtOnly, JpgOnly).
- Для тесту видалення синхронізуйте папки, видаліть файл на одній стороні, натисніть **Sync folder** ще раз і підтвердьте видалення в окремому діалозі. **Keep all** відкладатиме видалення без втрати manifest.
- Для перевірки конфлікту синхронізуйте `test.c`, змініть локальний файл, через Swagger `PUT /api/files/{id}?expectedVersion=...` змініть серверну копію та синхронізуйте ще раз. Обидві версії мають залишитися незмінними.
- Імена користувачів і паролі перевіряються під час реєстрації й авторизації. Повторна реєстрація показує конфлікт імені.


## Web (етап 3): Blazor WebAssembly

Проєкт `CloudDrive.Web` - клієнт, що працює повністю в браузері й спілкується з тим самим REST API (`CloudDrive.Client`,
`CloudDrive.Core`). Серверна частина не змінювалась. Технологія за вимогою етапу: **REST** (клієнт: Blazor WASM, без власного бекенду).

```text
Browser (Blazor WASM)  --HTTPS/JWT-->  CloudDrive.Server (ASP.NET Core, SQLite)   [https://myclouddrive.runasp.net]
```

### Запуск локально

```powershell
dotnet run --project CloudDrive.Web          # https://localhost:7180
```

Адреса API задається в `CloudDrive.Web/wwwroot/appsettings.json` (`ApiBaseUrl`, за замовчуванням задеплоєний сервер).
Щоб працювати з локальним сервером, створіть `wwwroot/appsettings.Development.json` з `{ "ApiBaseUrl": "http://localhost:5000/" }`
і запустіть `dotnet run --project CloudDrive.Server --urls http://localhost:5000`.

### Відповідність вимогам

| Вимога | Реалізація у вебі |
|---|---|
| FR-01 авторизація | вікно входу/реєстрації, JWT у `localStorage`, повернення на вхід при 401 |
| FR-02 список і атрибути | таблиця: назва, дати створення/зміни (локальний час), хто завантажив, хто редагував, розмір |
| FR-03 видимість колонок | меню **Columns** (колонка Name завжди видима), вибір запамʼятовується |
| FR-04 / FR-05 / FR-06 | **Upload** (кілька файлів, запит на заміну існуючого), **Download**, **Delete** з підтвердженням |
| FR-07 превʼю | `.c` як текст, `.jpg` як зображення; панель деталей зʼявляється **лише коли файл вибрано** |
| FR-08 сортування | заголовок **Name** і список A→Z / Z→A (сортує сервер); інші колонки сортуються локально |
| FR-09 фільтр | All / TXT + JPG / TXT only / JPG only (фільтрує сервер) |
| FR-10 синхронізація | **Sync folder**: той самий `SyncPlanner`, що й у desktop; локальна папка через File System Access API |
| FR-11 drag-and-drop | перетягування файлів з ОС у будь-яке місце вікна (overlay "Drop files to upload") |

Додатково: дерево папок (створення/видалення, контекстне меню), пошук по назві, статус-бар, адаптивна верстка.

> Синхронізація папок потребує File System Access API: **Chrome, Edge, Opera** на комп'ютері (HTTPS або localhost).
> Firefox і Safari показують пояснення; решта функцій працює в усіх сучасних браузерах.
> Папку не можна перетягнути для завантаження - для цілих папок використовуйте **Sync folder**.

### Публікація на MonsterASP.NET (бонус +5 б)

Вебклієнт - статичний сайт, тому розгортається **окремим сайтом** (безкоштовний план дозволяє кілька сайтів) поруч із сервером.
Сервер змінювати не потрібно: CORS уже дозволяє зовнішні origin-и.

1. У панелі MonsterASP створіть новий сайт (напр. `myclouddrive-web.runasp.net`), увімкніть HTTPS (Let's Encrypt).
2. Перевірте `ApiBaseUrl` у `CloudDrive.Web/wwwroot/appsettings.json`: це має бути **https**-адреса сервера
   (сторінка на HTTPS не може звертатися до HTTP API - браузер заблокує такі запити).
3. Опублікуйте:
   - Visual Studio: ПКМ на `CloudDrive.Web` -> **Publish** -> імпортуйте profile (`.PublishSettings`) нового сайту -> Publish;
   - або CLI: `dotnet publish CloudDrive.Web -c Release -o publish`, потім завантажте **вміст `publish/wwwroot`** у корінь сайту (FTP/SFTP).
4. Відкрийте `https://<ваш-сайт>.runasp.net`, увійдіть тим самим акаунтом, що й у desktop.
5. (Необов'язково) обмежте CORS на сервері змінною середовища `Cors__Origins__0=https://<ваш-сайт>.runasp.net`.

Усі адреси застосунку лишаються на `/` (папка передається параметром `?folder=`), тому сайт працює й без правил переписування шляхів на хості.

