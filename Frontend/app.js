// API Base URL
const API_BASE = window.location.protocol.startsWith("http")
  ? `${window.location.origin}/api`
  : "http://localhost:5000/api";

const PRIMARY_OWNER_ID = 1645991235;

// Global App State
let currentUser = {
  id: null,
  username: "Загрузка...",
  role: "User",
  isAuthorized: false
};

let currentScreen = "groupsScreen";
let currentGroupId = null;
let currentGroupName = "";

let cachedGroups = [];
let cachedUsers = [];
let cachedMessages = [];
let currentRoleFilter = "all";
let currentTaskFilter = "all"; // "all" | "pending" | "completed"
let currentGroupFilter = "all"; // "all" | "lectures" | "homework"

// Subject icon mapping based on name keywords
const SUBJECT_ICONS = [
  { keywords: ["история", "истор"], icon: "🏛️" },
  { keywords: ["математик", "матем", "алгебр"], icon: "📐" },
  { keywords: ["физик"], icon: "⚛️" },
  { keywords: ["химия", "хими"], icon: "🧪" },
  { keywords: ["биологи", "биол"], icon: "🧬" },
  { keywords: ["география", "геогр", "всесвит"], icon: "🌐" },
  { keywords: ["программирован", "програм", "информатик", "питон", "python", "js", "java"], icon: "💻" },
  { keywords: ["литератур"], icon: "📖" },
  { keywords: ["английск", "english", "язык", "deutsch"], icon: "🗣️" },
  { keywords: ["рисован", "изобраз", "искусств"], icon: "🎨" },
  { keywords: ["музык"], icon: "🎵" },
  { keywords: ["физкультур", "физвоспит", "спорт"], icon: "⚽" },
  { keywords: ["общее", "общ", "дз", "дедлайн"], icon: "✅" },
  { keywords: ["экономик", "эконом"], icon: "📊" },
  { keywords: ["философи"], icon: "🤔" },
];

function getSubjectIcon(name) {
  if (!name) return "📁";
  const lower = name.toLowerCase();
  for (const entry of SUBJECT_ICONS) {
    if (entry.keywords.some(k => lower.includes(k))) return entry.icon;
  }
  return "📁";
}

// Centralized API fetcher with Telegram HMAC-SHA256 initData
async function apiFetch(path, options = {}) {
  const initData = window.Telegram?.WebApp?.initData || "";
  const headers = {
    "Content-Type": "application/json",
    ...(options.headers || {})
  };
  if (initData) headers["Authorization"] = `Bearer ${initData}`;
  const res = await fetch(`${API_BASE}${path}`, { ...options, headers });
  return res;
}

// ============================================================
// THEME
// ============================================================
function initTheme() {
  const saved = localStorage.getItem("app-theme") || "dark";
  applyTheme(saved);
}

function applyTheme(theme) {
  document.documentElement.setAttribute("data-theme", theme);
  localStorage.setItem("app-theme", theme);

  const darkBtn = document.getElementById("darkThemeBtn");
  const lightBtn = document.getElementById("lightThemeBtn");
  if (darkBtn && lightBtn) {
    darkBtn.classList.toggle("active", theme === "dark");
    lightBtn.classList.toggle("active", theme === "light");
  }
}

// ============================================================
// DOM References
// ============================================================
const backBtn = document.getElementById("backBtn");
const headerTitle = document.getElementById("headerTitle");
const headerUsername = document.getElementById("headerUsername");
const userRoleBadge = document.getElementById("userRoleBadge");

const navItems = document.querySelectorAll(".nav-item");
const navUsersTab = document.getElementById("navUsersTab");
const navSettingsTab = document.getElementById("navSettingsTab");

const accessDeniedScreen = document.getElementById("accessDeniedScreen");
const deniedTelegramId = document.getElementById("deniedTelegramId");
const accessDeniedReason = document.getElementById("accessDeniedReason");
const copyMyIdBtn = document.getElementById("copyMyIdBtn");
const retryAccessBtn = document.getElementById("retryAccessBtn");

const groupsScreen = document.getElementById("groupsScreen");
const chatScreen = document.getElementById("chatScreen");
const usersScreen = document.getElementById("usersScreen");
const settingsScreen = document.getElementById("settingsScreen");

const groupsList = document.getElementById("groupsList");
const groupsCounter = document.getElementById("groupsCounter");
const groupSearchInput = document.getElementById("groupSearchInput");

const deadlinesSummaryCard = document.getElementById("deadlinesSummaryCard");
const deadlinesSummaryTitle = document.getElementById("deadlinesSummaryTitle");
const deadlinesSummaryText = document.getElementById("deadlinesSummaryText");

const currentChatName = document.getElementById("currentChatName");
const currentChatDesc = document.getElementById("currentChatDesc");
const chatAdminActions = document.getElementById("chatAdminActions");
const deleteCurrentGroupBtn = document.getElementById("deleteCurrentGroupBtn");
const messagesContainer = document.getElementById("messagesContainer");

const chatProgressText = document.getElementById("chatProgressText");
const chatProgressBar = document.getElementById("chatProgressBar");
const chatFilterTabs = document.querySelectorAll(".chat-filter-tab");
const countAllTasks = document.getElementById("countAllTasks");
const countPendingTasks = document.getElementById("countPendingTasks");
const countCompletedTasks = document.getElementById("countCompletedTasks");

const adminPostArea = document.getElementById("adminPostArea");
const adminMessageInput = document.getElementById("adminMessageInput");
const sendMessageBtn = document.getElementById("sendMessageBtn");

const toggleDeadlineBtn = document.getElementById("toggleDeadlineBtn");
const deadlineContainer = document.getElementById("deadlineContainer");
const adminDeadlineInput = document.getElementById("adminDeadlineInput");
const deadlinePreview = document.getElementById("deadlinePreview");
const presetBtns = document.querySelectorAll(".preset-btn");

const usersList = document.getElementById("usersList");
const userSearchInput = document.getElementById("userSearchInput");
const filterTabs = document.querySelectorAll(".filter-tab");
const addUserModalBtn = document.getElementById("addUserModalBtn");

const addUserModal = document.getElementById("addUserModal");
const closeModalBtn = document.getElementById("closeModalBtn");
const saveUserBtn = document.getElementById("saveUserBtn");
const manualUserId = document.getElementById("manualUserId");
const manualUserName = document.getElementById("manualUserName");
const manualUserRole = document.getElementById("manualUserRole");

const createGroupBtn = document.getElementById("createGroupBtn");
const newGroupName = document.getElementById("newGroupName");
const newGroupDesc = document.getElementById("newGroupDesc");

const darkThemeBtn = document.getElementById("darkThemeBtn");
const lightThemeBtn = document.getElementById("lightThemeBtn");
const createGroupSection = document.getElementById("createGroupSection");

// Theme buttons
if (darkThemeBtn) darkThemeBtn.addEventListener("click", () => applyTheme("dark"));
if (lightThemeBtn) lightThemeBtn.addEventListener("click", () => applyTheme("light"));

// ============================================================
// INIT
// ============================================================
async function initApp() {
  initTheme();

  const tg = window.Telegram?.WebApp;
  if (tg) {
    tg.ready();
    tg.expand();
    if (tg.setHeaderColor) tg.setHeaderColor("secondary_bg_color");
  }

  const fallbackId = tg?.initDataUnsafe?.user?.id || PRIMARY_OWNER_ID;

  try {
    const res = await apiFetch("/admin/me");
    if (res.ok) {
      const data = await res.json();
      currentUser.id = data.telegramId;
      currentUser.username = data.username || data.fullName || "Пользователь";
      currentUser.role = formatRoleName(data.role);
      currentUser.isAuthorized = true;
      syncRoleUI();
      await loadGroups();
    } else if (res.status === 403) {
      const err = await res.json().catch(() => ({}));
      showAccessDenied(err.telegramId || fallbackId);
    } else if (res.status === 401) {
      showAccessDenied(fallbackId, "Требуется авторизация Telegram WebApp.");
    } else {
      showAccessDenied(fallbackId, "Ошибка проверки профиля на сервере.");
    }
  } catch (err) {
    console.error("Init failed:", err);
    showAccessDenied(fallbackId, "Не удалось подключиться к серверу.");
  }
}

function showAccessDenied(telegramId, customMessage) {
  document.querySelectorAll(".screen").forEach((s) => s.classList.remove("active"));
  if (accessDeniedScreen) accessDeniedScreen.classList.add("active");
  if (deniedTelegramId) deniedTelegramId.textContent = telegramId;
  if (customMessage && accessDeniedReason) accessDeniedReason.textContent = customMessage;
  navItems.forEach((btn) => btn.classList.add("hidden"));
  if (userRoleBadge) {
    userRoleBadge.textContent = "⛔ Ограничен";
    userRoleBadge.className = "role-badge user";
  }
  if (headerUsername) headerUsername.textContent = "Доступ не подтвержден";
}

if (copyMyIdBtn) {
  copyMyIdBtn.addEventListener("click", () => {
    const id = deniedTelegramId?.textContent;
    if (id) {
      navigator.clipboard.writeText(id).then(() => {
        copyMyIdBtn.textContent = "✅ Скопировано!";
        setTimeout(() => { copyMyIdBtn.textContent = "📋 Скопировать ID"; }, 2000);
      });
    }
  });
}

if (retryAccessBtn) {
  retryAccessBtn.addEventListener("click", () => window.location.reload());
}

function formatRoleName(roleNumOrStr) {
  if (roleNumOrStr === 0 || roleNumOrStr === "Developer") return "Developer";
  if (roleNumOrStr === 1 || roleNumOrStr === "Admin") return "Admin";
  return "User";
}

async function handleResponseError(res, defaultMsg) {
  try {
    const data = await res.json();
    if (data && data.error) { alert(data.error); return; }
  } catch {}
  alert(defaultMsg);
}

// ============================================================
// SYNC ROLE UI
// ============================================================
function syncRoleUI() {
  let roleText = "🎓 Студент";
  let roleClass = "user";
  if (currentUser.role === "Developer") {
    roleText = "⟨/⟩ Разработчик";
    roleClass = "dev";
  } else if (currentUser.role === "Admin") {
    roleText = "👑 Администратор";
    roleClass = "admin";
  }

  if (userRoleBadge) {
    userRoleBadge.textContent = roleText;
    userRoleBadge.className = `role-badge ${roleClass}`;
  }

  if (headerUsername) {
    headerUsername.textContent = currentUser.username || "Пользователь";
  }

  // Nav tabs visibility
  if (currentUser.role === "User") {
    navUsersTab.classList.add("hidden");
    navSettingsTab.classList.remove("hidden"); // students can see settings for theme
  } else if (currentUser.role === "Admin") {
    navUsersTab.classList.remove("hidden");
    navSettingsTab.classList.remove("hidden");
  } else {
    navUsersTab.classList.remove("hidden");
    navSettingsTab.classList.remove("hidden");
  }

  // Developer-only: create group section
  if (createGroupSection) {
    if (currentUser.role === "Developer") {
      createGroupSection.classList.remove("hidden");
    } else {
      createGroupSection.classList.add("hidden");
    }
  }

  // Chat input (only for Admin and Developer)
  if ((currentUser.role === "Admin" || currentUser.role === "Developer") && currentGroupId) {
    adminPostArea.classList.remove("hidden");
  } else {
    adminPostArea.classList.add("hidden");
  }

  // Chat delete subject button (only for Developer)
  if (currentUser.role === "Developer" && currentGroupId) {
    chatAdminActions.classList.remove("hidden");
  } else {
    chatAdminActions.classList.add("hidden");
  }
}

// ============================================================
// NAVIGATION
// ============================================================
navItems.forEach((btn) => {
  btn.addEventListener("click", () => {
    const targetScreen = btn.dataset.screen;
    switchScreen(targetScreen);
  });
});

function switchScreen(screenId) {
  currentScreen = screenId;
  currentGroupId = null;

  document.querySelectorAll(".screen").forEach((s) => s.classList.remove("active"));
  const target = document.getElementById(screenId);
  if (target) target.classList.add("active");

  navItems.forEach((btn) => {
    btn.classList.toggle("active", btn.dataset.screen === screenId);
  });

  backBtn.classList.add("hidden");

  if (screenId === "groupsScreen") {
    headerTitle.textContent = "Предметы";
    loadGroups();
  } else if (screenId === "usersScreen") {
    headerTitle.textContent = "Вайтлист";
    loadUsers();
  } else if (screenId === "settingsScreen") {
    headerTitle.textContent = "Настройки";
  }

  syncRoleUI();
}

backBtn.addEventListener("click", () => {
  switchScreen("groupsScreen");
});

// ============================================================
// GROUPS / SUBJECTS
// ============================================================

// Groups filter tabs
const groupsFilterTabs = document.querySelectorAll(".groups-filter-tab");
groupsFilterTabs.forEach((tab) => {
  tab.addEventListener("click", () => {
    groupsFilterTabs.forEach((t) => t.classList.remove("active"));
    tab.classList.add("active");
    currentGroupFilter = tab.dataset.groupFilter;
    renderGroupsList(cachedGroups);
  });
});

async function loadGroups() {
  groupsList.innerHTML = '<div class="loader">Загрузка предметов...</div>';
  try {
    const res = await apiFetch("/admin/groups");
    if (!res.ok) throw new Error("Не удалось загрузить группы");
    cachedGroups = await res.json();
    renderGroupsList(cachedGroups);
    await loadDeadlinesSummary();
  } catch (err) {
    console.error(err);
    groupsList.innerHTML = `<div class="empty-state">⚠️ Ошибка связи с API (${API_BASE}). Запустите backend.</div>`;
  }
}

async function loadDeadlinesSummary() {
  if (!deadlinesSummaryCard) return;
  try {
    const res = await apiFetch("/admin/my-deadlines");
    if (!res.ok) return;
    const items = await res.json();

    if (!items || items.length === 0) {
      deadlinesSummaryCard.classList.add("hidden");
      return;
    }

    const uncompleted = items.filter((d) => !d.isCompleted);
    const now = new Date();
    const urgent = uncompleted.filter((d) => {
      const diffHours = (new Date(d.deadline) - now) / (1000 * 60 * 60);
      return diffHours <= 48;
    });

    deadlinesSummaryCard.classList.remove("hidden");
    if (urgent.length > 0) {
      deadlinesSummaryTitle.textContent = `⚠️ Горящие дедлайны (${urgent.length})`;
      const subjects = [...new Set(urgent.map((u) => u.groupName))].join(", ");
      deadlinesSummaryText.textContent = `Срочно сдать задания по: ${subjects}`;
      deadlinesSummaryCard.style.borderColor = "rgba(255,159,10,0.5)";
    } else {
      deadlinesSummaryTitle.textContent = `⏳ Ближайшие задания (${uncompleted.length})`;
      deadlinesSummaryText.textContent = `Выполнено ${items.length - uncompleted.length} из ${items.length} дедлайнов`;
      deadlinesSummaryCard.style.borderColor = "";
    }
  } catch (err) {
    console.warn("Could not load deadlines summary:", err);
  }
}

function renderGroupsList(groups) {
  const query = groupSearchInput.value.toLowerCase().trim();

  let filtered = groups.filter((g) =>
    g.name.toLowerCase().includes(query) ||
    (g.description && g.description.toLowerCase().includes(query))
  );

  // Apply group type filter (cosmetic — based on name keywords)
  if (currentGroupFilter === "lectures") {
    filtered = filtered.filter((g) =>
      /лекц|lecture/i.test(g.name + " " + (g.description || ""))
    );
  } else if (currentGroupFilter === "homework") {
    filtered = filtered.filter((g) =>
      /дз|домаш|задан|homework/i.test(g.name + " " + (g.description || ""))
    );
  }

  groupsCounter.textContent = filtered.length;

  if (filtered.length === 0) {
    groupsList.innerHTML = '<div class="empty-state">Предметы не найдены.</div>';
    return;
  }

  groupsList.innerHTML = "";
  filtered.forEach((g) => {
    const item = document.createElement("div");
    item.className = "group-item";

    const icon = getSubjectIcon(g.name);
    // Message count badge (only if > 0)
    const countBadgeHtml = g.messageCount > 0
      ? `<span class="group-count-badge">${g.messageCount}</span>`
      : "";

    item.innerHTML = `
      <div class="group-avatar">${icon}</div>
      <div class="group-info">
        <h3>${escapeHtml(g.name)}</h3>
        <p>${escapeHtml(g.description || "Задания и дедлайны")}</p>
      </div>
      <div class="group-right">
        ${countBadgeHtml}
        <span class="arrow-icon">›</span>
      </div>
    `;
    item.addEventListener("click", () => openChat(g.id, g.name, g.description));
    groupsList.appendChild(item);
  });
}

groupSearchInput.addEventListener("input", () => renderGroupsList(cachedGroups));

// ============================================================
// CHAT / MESSAGES
// ============================================================
async function openChat(groupId, name, desc) {
  currentGroupId = groupId;
  currentGroupName = name;
  currentTaskFilter = "all";

  chatFilterTabs.forEach((t) => t.classList.toggle("active", t.dataset.taskFilter === "all"));

  headerTitle.textContent = name;
  currentChatName.textContent = name;
  currentChatDesc.textContent = desc || "Все актуальные задания и дедлайны";

  document.querySelectorAll(".screen").forEach((s) => s.classList.remove("active"));
  chatScreen.classList.add("active");
  backBtn.classList.remove("hidden");

  // Update nav active state
  navItems.forEach((btn) => btn.classList.remove("active"));

  if (adminDeadlineInput) adminDeadlineInput.value = "";
  if (deadlineContainer) deadlineContainer.classList.add("hidden");
  updateDeadlinePreview();

  syncRoleUI();
  await loadMessages(groupId);
}

// Deadline Picker
if (toggleDeadlineBtn) {
  toggleDeadlineBtn.addEventListener("click", () => {
    if (deadlineContainer) deadlineContainer.classList.toggle("hidden");
  });
}

function updateDeadlinePreview() {
  if (!deadlinePreview) return;
  if (!adminDeadlineInput || !adminDeadlineInput.value) {
    deadlinePreview.textContent = "Без срока";
  } else {
    const d = new Date(adminDeadlineInput.value);
    deadlinePreview.textContent = d.toLocaleDateString("ru-RU", {
      day: "numeric", month: "short", hour: "2-digit", minute: "2-digit"
    });
  }
}

if (adminDeadlineInput) {
  adminDeadlineInput.addEventListener("change", updateDeadlinePreview);
}

presetBtns.forEach((btn) => {
  btn.addEventListener("click", () => {
    const preset = btn.dataset.preset;
    const now = new Date();
    if (preset === "tomorrow") {
      now.setDate(now.getDate() + 1);
      now.setHours(23, 59, 0, 0);
      adminDeadlineInput.value = formatInputDateTime(now);
    } else if (preset === "3days") {
      now.setDate(now.getDate() + 3);
      now.setHours(23, 59, 0, 0);
      adminDeadlineInput.value = formatInputDateTime(now);
    } else if (preset === "1week") {
      now.setDate(now.getDate() + 7);
      now.setHours(23, 59, 0, 0);
      adminDeadlineInput.value = formatInputDateTime(now);
    } else if (preset === "clear") {
      adminDeadlineInput.value = "";
    }
    updateDeadlinePreview();
  });
});

function formatInputDateTime(date) {
  const pad = (n) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

// Load Messages
async function loadMessages(groupId) {
  messagesContainer.innerHTML = '<div class="loader">Загрузка заданий...</div>';
  try {
    const res = await apiFetch(`/admin/group/${groupId}/messages`);
    cachedMessages = await res.json();
    updateTaskProgress();
    renderFilteredMessages();
  } catch (err) {
    console.error(err);
    messagesContainer.innerHTML = '<div class="empty-state">Ошибка загрузки заданий</div>';
  }
}

// Update Task Progress
function updateTaskProgress() {
  const total = cachedMessages.length;
  const completed = cachedMessages.filter((m) => m.isCompleted).length;
  const pending = total - completed;
  const percent = total > 0 ? Math.round((completed / total) * 100) : 0;

  if (countAllTasks) countAllTasks.textContent = total;
  if (countPendingTasks) countPendingTasks.textContent = pending;
  if (countCompletedTasks) countCompletedTasks.textContent = completed;

  if (chatProgressText) chatProgressText.textContent = `Сдано ${completed} из ${total} заданий (${percent}%)`;
  if (chatProgressBar) chatProgressBar.style.width = `${percent}%`;
}

// Chat Filter Tabs
chatFilterTabs.forEach((tab) => {
  tab.addEventListener("click", () => {
    chatFilterTabs.forEach((t) => t.classList.remove("active"));
    tab.classList.add("active");
    currentTaskFilter = tab.dataset.taskFilter;
    renderFilteredMessages();
  });
});

// Render messages with Markdown support
function renderFilteredMessages() {
  let filtered = cachedMessages;
  if (currentTaskFilter === "pending") {
    filtered = cachedMessages.filter((m) => !m.isCompleted);
  } else if (currentTaskFilter === "completed") {
    filtered = cachedMessages.filter((m) => m.isCompleted);
  }

  if (filtered.length === 0) {
    let msg = "Пока нет записей. Администратор ещё не опубликовал ДЗ.";
    if (currentTaskFilter === "pending") msg = "🎉 Отлично! Все задания по этому предмету выполнены.";
    if (currentTaskFilter === "completed") msg = "Пока нет выполненных заданий.";
    messagesContainer.innerHTML = `<div class="empty-state">${msg}</div>`;
    return;
  }

  messagesContainer.innerHTML = "";
  const canDelete = currentUser.role === "Developer" || currentUser.role === "Admin";

  // Configure marked for safe rendering
  if (typeof marked !== "undefined") {
    marked.setOptions({ breaks: true, gfm: true });
  }

  filtered.forEach((m) => {
    const card = document.createElement("div");
    card.className = `message-card ${m.isCompleted ? "completed" : ""}`;

    const date = new Date(m.sentAt);
    const dateFormatted = date.toLocaleDateString("ru-RU", { day: "2-digit", month: "short" });
    const timeFormatted = date.toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" });

    const deadlineBadgeHtml = formatDeadlineBadge(m.deadline, m.isCompleted);

    // Render text with Markdown if available, else escape
    let textHtml;
    if (typeof marked !== "undefined") {
      textHtml = marked.parse(m.text || "");
    } else {
      textHtml = escapeHtml(m.text);
    }

    card.innerHTML = `
      <div class="message-card-header">
        <span class="message-author">✍️ ${escapeHtml(m.author)}</span>
        <div>
          <span class="message-date">${dateFormatted}, ${timeFormatted}</span>
          ${canDelete ? `<button class="delete-msg-btn" data-msg-id="${m.id}" title="Удалить запись">✕</button>` : ""}
        </div>
      </div>
      <div class="message-text">${textHtml}</div>
      <div class="message-footer-row">
        ${deadlineBadgeHtml ? `<div>${deadlineBadgeHtml}</div>` : "<div></div>"}
        <button class="task-toggle-btn ${m.isCompleted ? "completed" : ""}" data-toggle-id="${m.id}">
          ${m.isCompleted ? "✅ Сдано" : "⬜ Отметить сданным"}
        </button>
      </div>
    `;

    const toggleBtn = card.querySelector(".task-toggle-btn");
    if (toggleBtn) toggleBtn.addEventListener("click", () => toggleTaskComplete(m.id));

    if (canDelete) {
      const delBtn = card.querySelector(".delete-msg-btn");
      if (delBtn) delBtn.addEventListener("click", () => deleteMessage(m.id));
    }

    messagesContainer.appendChild(card);
  });

  messagesContainer.scrollTop = messagesContainer.scrollHeight;
}

// Toggle Task Complete
async function toggleTaskComplete(msgId) {
  if (window.Telegram?.WebApp?.HapticFeedback) {
    window.Telegram.WebApp.HapticFeedback.impactOccurred("medium");
  }
  try {
    const res = await apiFetch(`/admin/message/${msgId}/toggle-complete`, { method: "POST" });
    if (res.ok) {
      const data = await res.json();
      const msg = cachedMessages.find((m) => m.id === msgId);
      if (msg) {
        msg.isCompleted = data.isCompleted;
        msg.completedAt = data.completedAt;
        updateTaskProgress();
        renderFilteredMessages();
        await loadDeadlinesSummary();
      }
    }
  } catch (err) {
    console.error("Toggle complete error:", err);
  }
}

// Format Deadline Badge
function formatDeadlineBadge(deadlineStr, isCompleted) {
  if (!deadlineStr) return "";
  const dl = new Date(deadlineStr);
  const now = new Date();
  const diffMs = dl - now;
  const diffHours = diffMs / (1000 * 60 * 60);
  const diffDays = Math.ceil(diffHours / 24);
  const formattedDate = dl.toLocaleDateString("ru-RU", {
    day: "numeric", month: "short", hour: "2-digit", minute: "2-digit"
  });

  if (isCompleted) return `<span class="deadline-badge done">✅ Сдано (срок: ${formattedDate})</span>`;
  if (diffHours < 0) return `<span class="deadline-badge overdue">🔥 Просрочено (${formattedDate})</span>`;
  if (diffHours < 24) {
    const hoursLeft = Math.max(1, Math.round(diffHours));
    return `<span class="deadline-badge urgent">⚠️ Срок: ${formattedDate} (осталось ${hoursLeft} ч)</span>`;
  }
  return `<span class="deadline-badge normal">⏳ Срок: ${formattedDate} (осталось ${diffDays} дн)</span>`;
}

// Send Message
sendMessageBtn.addEventListener("click", async () => {
  const text = adminMessageInput.value.trim();
  if (!text || !currentGroupId) return;
  const deadline = adminDeadlineInput?.value ? new Date(adminDeadlineInput.value).toISOString() : null;

  sendMessageBtn.disabled = true;
  try {
    const res = await apiFetch(`/admin/group/${currentGroupId}/message`, {
      method: "POST",
      body: JSON.stringify({ text, deadline })
    });
    if (res.ok) {
      adminMessageInput.value = "";
      if (adminDeadlineInput) adminDeadlineInput.value = "";
      if (deadlineContainer) deadlineContainer.classList.add("hidden");
      updateDeadlinePreview();
      await loadMessages(currentGroupId);
      await loadDeadlinesSummary();
    } else {
      await handleResponseError(res, "Не удалось отправить сообщение");
    }
  } catch (e) {
    console.error(e);
    alert("Ошибка соединения");
  } finally {
    sendMessageBtn.disabled = false;
  }
});

// Delete Message
async function deleteMessage(id) {
  if (!confirm("Удалить это задание?")) return;
  try {
    const res = await apiFetch(`/admin/message/${id}`, { method: "DELETE" });
    if (res.ok) {
      await loadMessages(currentGroupId);
      await loadDeadlinesSummary();
    } else {
      await handleResponseError(res, "Не удалось удалить задание");
    }
  } catch (e) {
    console.error(e);
  }
}

// Delete Entire Subject
deleteCurrentGroupBtn.addEventListener("click", async () => {
  if (!confirm(`Вы действительно хотите удалить предмет «${currentGroupName}» и все его задания?`)) return;
  try {
    const res = await apiFetch(`/admin/group/${currentGroupId}`, { method: "DELETE" });
    if (res.ok) {
      switchScreen("groupsScreen");
    } else {
      await handleResponseError(res, "Не удалось удалить предмет");
    }
  } catch (e) {
    console.error(e);
    alert("Ошибка соединения");
  }
});

// ============================================================
// USERS & WHITELIST
// ============================================================
async function loadUsers() {
  usersList.innerHTML = '<div class="loader">Загрузка участников...</div>';
  try {
    const res = await apiFetch("/admin/users");
    if (!res.ok) throw new Error("Не удалось загрузить участников");
    cachedUsers = await res.json();
    renderUsersList(cachedUsers);
  } catch (err) {
    console.error(err);
    usersList.innerHTML = '<div class="empty-state">Ошибка загрузки участников</div>';
  }
}

function renderUsersList(users) {
  const query = userSearchInput.value.toLowerCase().trim();
  let filtered = users.filter((u) => {
    const matchesSearch =
      (u.fullName && u.fullName.toLowerCase().includes(query)) ||
      (u.username && u.username.toLowerCase().includes(query)) ||
      String(u.telegramId).includes(query);
    const roleName = formatRoleName(u.role);
    const matchesRole = currentRoleFilter === "all" || roleName === currentRoleFilter;
    return matchesSearch && matchesRole;
  });

  if (filtered.length === 0) {
    usersList.innerHTML = '<div class="empty-state">Участники не найдены</div>';
    return;
  }

  usersList.innerHTML = "";
  const isDev = currentUser.role === "Developer";

  filtered.forEach((u) => {
    const roleName = formatRoleName(u.role);
    const card = document.createElement("div");
    card.className = "user-card";

    let roleClass = "user";
    let roleLabel = "Студент";
    if (roleName === "Developer") { roleClass = "dev"; roleLabel = "Разработчик"; }
    else if (roleName === "Admin") { roleClass = "admin"; roleLabel = "Администратор"; }

    const isSelf = u.telegramId === currentUser.id || u.telegramId === PRIMARY_OWNER_ID;
    const initial = (u.fullName || u.username || "U")[0].toUpperCase();

    card.innerHTML = `
      <div class="user-avatar">${initial}</div>
      <div class="user-info">
        <div class="user-name-row">
          <span class="user-name">${escapeHtml(u.fullName || u.username)}</span>
          <span class="role-pill ${roleClass}">${roleLabel}</span>
        </div>
        <div class="user-meta">ID: <code>${u.telegramId}</code> ${u.username ? `• @${escapeHtml(u.username)}` : ""}</div>
      </div>
      <div class="user-actions">
        ${isDev && !isSelf ? `
          <select class="user-role-select" data-user-id="${u.telegramId}">
            <option value="User" ${roleName === "User" ? "selected" : ""}>Студент</option>
            <option value="Admin" ${roleName === "Admin" ? "selected" : ""}>Админ</option>
            <option value="Developer" ${roleName === "Developer" ? "selected" : ""}>Разработчик</option>
          </select>
          <button class="delete-user-btn" data-del-user="${u.telegramId}" title="Удалить из системы">🗑️</button>
        ` : ""}
      </div>
    `;

    const select = card.querySelector(".user-role-select");
    if (select) select.addEventListener("change", async (e) => changeUserRole(u.telegramId, e.target.value));

    const delBtn = card.querySelector(".delete-user-btn");
    if (delBtn) delBtn.addEventListener("click", async () => deleteUser(u.telegramId, u.fullName || u.username));

    usersList.appendChild(card);
  });
}

async function changeUserRole(telegramId, role) {
  try {
    const res = await apiFetch(`/admin/user/${telegramId}/role/${role}`, { method: "PATCH" });
    if (res.ok) {
      await loadUsers();
    } else {
      const err = await res.json();
      alert(err.message || "Ошибка смены роли");
    }
  } catch (e) {
    console.error(e);
    alert("Ошибка связи");
  }
}

async function deleteUser(telegramId, name) {
  if (!confirm(`Удалить участника «${name}» (ID: ${telegramId}) из вайтлиста?`)) return;
  try {
    const res = await apiFetch(`/admin/user/${telegramId}`, { method: "DELETE" });
    if (res.ok) {
      await loadUsers();
    } else {
      const err = await res.json();
      alert(err.message || "Не удалось удалить участника");
    }
  } catch (e) {
    console.error(e);
    alert("Ошибка соединения");
  }
}

filterTabs.forEach((tab) => {
  tab.addEventListener("click", () => {
    filterTabs.forEach((t) => t.classList.remove("active"));
    tab.classList.add("active");
    currentRoleFilter = tab.dataset.role;
    renderUsersList(cachedUsers);
  });
});

userSearchInput.addEventListener("input", () => renderUsersList(cachedUsers));

// ============================================================
// MODAL: ADD USER
// ============================================================
addUserModalBtn.addEventListener("click", () => addUserModal.classList.remove("hidden"));
closeModalBtn.addEventListener("click", () => addUserModal.classList.add("hidden"));

saveUserBtn.addEventListener("click", async () => {
  const id = parseInt(manualUserId.value);
  const name = manualUserName.value.trim();
  const role = manualUserRole.value;

  if (!id || id <= 0) { alert("Введите корректный числовой Telegram ID!"); return; }

  try {
    const res = await apiFetch("/admin/user", {
      method: "POST",
      body: JSON.stringify({
        telegramId: id,
        username: name || `User_${id}`,
        fullName: name || `Пользователь ${id}`,
        role: role
      })
    });
    if (res.ok) {
      manualUserId.value = "";
      manualUserName.value = "";
      addUserModal.classList.add("hidden");
      await loadUsers();
    } else {
      await handleResponseError(res, "Ошибка при сохранении пользователя");
    }
  } catch (e) {
    console.error(e);
    alert("Ошибка соединения");
  }
});

// ============================================================
// SETTINGS: CREATE GROUP (Developer only)
// ============================================================
if (createGroupBtn) {
  createGroupBtn.addEventListener("click", async () => {
    const name = newGroupName.value.trim();
    const desc = newGroupDesc.value.trim();
    if (!name) { alert("Введите название предмета!"); return; }

    createGroupBtn.disabled = true;
    try {
      const res = await apiFetch("/admin/group", {
        method: "POST",
        body: JSON.stringify({ name, description: desc })
      });
      if (res.ok) {
        newGroupName.value = "";
        newGroupDesc.value = "";
        alert(`Предмет «${name}» успешно создан!`);
        switchScreen("groupsScreen");
      } else {
        await handleResponseError(res, "Не удалось создать предмет");
      }
    } catch (e) {
      console.error(e);
      alert("Ошибка связи");
    } finally {
      createGroupBtn.disabled = false;
    }
  });
}

// ============================================================
// UTILITY
// ============================================================
function escapeHtml(str) {
  if (!str) return "";
  return String(str)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#039;");
}

// ============================================================
// START
// ============================================================
initApp();
