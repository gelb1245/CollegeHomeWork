# Telegram Homework Bot & Mini App 🎓

Система управления домашними заданиями и предметами для колледжа:
- **Backend**: C# (ASP.NET Core 8 Web API + Entity Framework Core).
- **Telegram Bot**: Telegram.Bot v22, режим polling или webhook.
- **Frontend / Mini App**: HTML5, CSS3, JavaScript (Telegram WebApp SDK).
- **База данных**: PostgreSQL (Neon) в продакшене, SQLite локально.
- **Файлы к заданиям** (фото, PDF) хранятся в Telegram: в базе только `file_id`.

---

## 🗂 Структура проекта

```
CollegeHomeWork/
├── start_backend.sh / .bat        # Запуск локально (macOS·Linux / Windows)
├── CollegeHomeWork.sln            # Решение: Backend + Tests
│
├── Backend/                       # ASP.NET Core 8
│   ├── Controllers/
│   │   ├── HomeworkController.cs  # Задания: список, публикация с файлами, редактирование, «Сдано», дедлайны
│   │   ├── AdminController.cs     # Профиль, предметы, пользователи и роли
│   │   ├── FilesController.cs     # Отдача фото/PDF по подписанной ссылке
│   │   └── BotController.cs       # Webhook Telegram (в режиме webhook)
│   ├── Services/
│   │   ├── Bot/                   # Бот: BotUpdateHandler (команды), BotAccessManager (вайтлист, роли),
│   │   │                          #      BotUpdatesService (режим polling/webhook/off), MiniAppLink
│   │   ├── AttachmentStorage.cs   # Загрузка файлов в Telegram и проверка их типа
│   │   ├── AttachmentUrlSigner.cs # Временные подписанные ссылки на файлы
│   │   ├── HomeworkNotificationService.cs # Уведомления студентам (самоудаление через 24 ч)
│   │   └── TelegramAuthService.cs # Проверка подписи initData Telegram Mini App
│   ├── Data/                      # DbContext, выбор БД, миграции при старте
│   ├── Migrations/                # Миграции SQLite и Migrations/Postgres
│   └── Models/                    # User, ChatGroup, Message, Attachment, ...
│
├── Frontend/                      # Telegram Mini App (index.html, style.css, app.js)
└── Tests/CollegeHomeWork.Tests/   # xUnit-тесты
```

---

## 🚀 Быстрый старт

### 1. Настройка токена
> ⚠️ **Никогда не коммитьте токен в репозиторий.** `appsettings.json` содержит только пустое значение.

**Локально:** скопируйте `Backend/appsettings.Development.json.example` в `Backend/appsettings.Development.json` (файл в `.gitignore`) и вставьте токен от `@BotFather`:
```json
{
  "Telegram": {
    "BotToken": "ВАШ_ТОКЕН_ОТ_BOTFATHER"
  }
}
```

**На сервере (Render/Docker):** задайте переменные окружения:

| Переменная | Значение |
|---|---|
| `TELEGRAM_BOT_TOKEN` | токен бота |
| `DATABASE_URL` | строка подключения PostgreSQL, напр. `postgresql://user:pass@host/db?sslmode=require` (Neon/Supabase). Если не задана — используется локальный SQLite |
| `BOT_UPDATES_MODE` | `polling` (по умолчанию), `webhook` или `off` — см. ниже |
| `TELEGRAM_WEBHOOK_SECRET` | секрет для режима `webhook` (любая длинная случайная строка) |
| `TELEGRAM_STORAGE_CHAT_ID` | (опционально) чат, куда бот складывает файлы заданий. По умолчанию — чат админа, который загрузил файл |
| `APP_TIME_ZONE` | часовой пояс для времени в сообщениях бота, по умолчанию `Europe/Kyiv` |
| `TELEGRAM_API_URL` | (опционально) свой Bot API-сервер: снимает лимит 20 МБ на файлы |

Без токена приложение не запустится и выведет понятную ошибку.

### База данных
Провайдер выбирается автоматически:
- **PostgreSQL** — если задана переменная `DATABASE_URL` (формат `postgresql://...`). Используется в продакшене.
- **SQLite** (`Backend/telegrambot.db`) — если переменной нет. Удобно для локальной разработки.

Схема управляется **только миграциями EF Core** и применяется автоматически при старте. Миграции раздельные: `Backend/Migrations` (SQLite) и `Backend/Migrations/Postgres`. После изменения моделей создайте миграцию для обоих:
```bash
dotnet tool restore
cd Backend
dotnet ef migrations add ИмяМиграции --context TelegramBotContext
dotnet ef migrations add ИмяМиграции --context PostgresTelegramBotContext --output-dir Migrations/Postgres
```
Файл `telegrambot.db` содержит личные данные студентов и не хранится в git.

### 2. Вайтлист
В [Backend/whitelist.json](Backend/whitelist.json) уже добавлен Telegram ID владельца (`8663026875`, задаётся константой `PrimaryOwnerId` в `Backend/Services/WhitelistService.cs`). Вы можете добавлять других пользователей через запятую:
```json
[
  1645991235,
  123456789
]
```

### 3. Запуск сервера
- **macOS / Linux:** `./start_backend.sh`
- **Windows:** `start_backend.bat`

Сервер запустится на `http://localhost:5000` и раздаёт:
- **Мини-приложение**: `http://localhost:5000/`
- **Swagger документацию API**: `http://localhost:5000/swagger`

Локально бот **не получает сообщения** (`BOT_UPDATES_MODE=off`): иначе он перехватывал бы их у бота на сервере. Чтобы проверить команды бота локально: `BOT_UPDATES_MODE=polling ./start_backend.sh` (на это время бот на сервере перестанет отвечать).

### Режимы бота
| `BOT_UPDATES_MODE` | Как работает |
|---|---|
| `polling` | приложение само спрашивает Telegram о новых сообщениях. Работает везде |
| `webhook` | Telegram присылает сообщения на `/api/bot/webhook`. Нужны публичный HTTPS-адрес (`Telegram:WebAppUrl`) и `TELEGRAM_WEBHOOK_SECRET` |
| `off` | бот не получает сообщения (уведомления и файлы при этом работают) |

### 4. Тесты
```bash
dotnet test CollegeHomeWork.sln
```

---

## 📱 Роли и функционал

1. **Обычный пользователь (User / Студент)**:
   - Проверяется по вайтлисту.
   - В Telegram может отправить `/list` и получить список предметов.
   - В Mini App открывает нужный предмет («История», «Всесвит», «ДЗ») и видит все задания, дату и время их публикации администратором (режим только для чтения).

2. **Администратор (Admin)**:
   - Может писать сообщения в чаты предметов:
     - Из Telegram: `/post <ID_группы> <текст задания>`
     - Или прямо из Mini App через поле ввода.

3. **Разработчик (Developer)**:
   - Полное управление через HTTP API или Mini App:
     - Создание новых групп/предметов (`POST /api/admin/group`).
     - Удаление предметов (`DELETE /api/admin/group/{id}`).
     - Назначение ролей (`PATCH /api/admin/user/{telegramId}/role/{Admin|Developer|User}`).
