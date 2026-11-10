# Telegram Homework Bot & Mini App 🎓

Система управления домашними заданиями и предметами для колледжа:
- **Backend**: C# (ASP.NET Core 8 Web API + Entity Framework Core SQLite).
- **Telegram Bot**: интеграция с HTTP Webhook API Telegram (Telegram.Bot v22).
- **Frontend / Mini App**: HTML5, CSS3, JavaScript (Telegram WebApp SDK).
- **База данных**: SQLite (`telegrambot.db`).

---

## 🗂 Структура проекта

```
collegehomework/
│
├── start_backend.bat             # Скрипт быстрого запуска бэкенда в 1 клик
├── README.md                     # Инструкция
│
├── Backend/                      # Серверная часть (C# .NET 8)
│   ├── Controllers/
│   │   ├── BotController.cs      # Webhook бота, whitelist, роли, команды /start, /post, /list
│   │   └── AdminController.cs    # REST API: CRUD групп, сообщения, роли, пользователи
│   ├── Data/
│   │   └── TelegramBotContext.cs # EF Core DbContext
│   ├── Models/
│   │   ├── User.cs               # Модель пользователя (Developer, Admin, User)
│   │   ├── ChatGroup.cs          # Предметы/группы (История, Всесвит, ДЗ...)
│   │   └── Message.cs            # Записи и домашние задания с датой/временем
│   ├── appsettings.json          # Токен бота и адрес WebApp
│   ├── whitelist.json            # Список разрешенных Telegram ID (вайтлист)
│   ├── telegrambot.db            # База данных SQLite (уже создана и обновлена)
│   └── Program.cs                # Настройка ASP.NET Core, CORS и раздачи статики
│
└── Frontend/                     # Мини-приложение (Telegram Mini App)
    ├── index.html                # Интерфейс чатов, списка предметов и постов
    ├── style.css                 # Стили в дизайне Telegram (темная/светлая тема)
    └── app.js                    # Логика загрузки сообщений, ролей и API
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
| `ConnectionStrings__Default` | (опционально) путь к БД на постоянном диске, напр. `Data Source=/var/data/telegrambot.db` |

Без токена приложение не запустится и выведет понятную ошибку.

### База данных
Схема управляется **только миграциями EF Core** (`Backend/Migrations`) и применяется автоматически при старте. Новая миграция после изменения моделей:
```bash
dotnet tool restore
cd Backend && dotnet ef migrations add ИмяМиграции
```
Файл `telegrambot.db` содержит личные данные студентов и не хранится в git.

### 2. Вайтлист
В [Backend/whitelist.json](Backend/whitelist.json) уже добавлен ваш Telegram ID (`1645991235`). Вы можете добавлять других пользователей через запятую:
```json
[
  1645991235,
  123456789
]
```

### 3. Запуск сервера
Запустите файл [start_backend.bat](start_backend.bat) (или выполните `dotnet run` в папке `Backend`).

Сервер запустится на `https://localhost:5001` и сразу раздает:
- **Мини-приложение**: `https://localhost:5001/`
- **Swagger документацию API**: `https://localhost:5001/swagger`
- **Webhook для Telegram**: `https://localhost:5001/api/bot/webhook`

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
