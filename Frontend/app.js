// API Base URL
const API_BASE = window.location.protocol.startsWith("http")
  ? `${window.location.origin}/api`
  : "http://localhost:5000/api";

const PRIMARY_OWNER_ID = 8663026875;

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

// Line icons (stroke SVG, Lucide-style)
const ICONS = {
  landmark: '<path d="M3 21h18"/><path d="M5 21V10"/><path d="M19 21V10"/><path d="M9 21v-6a3 3 0 0 1 6 0v6"/><path d="M3 10 12 4l9 6"/>',
  globe: '<circle cx="12" cy="12" r="9"/><path d="M12 3a14 14 0 0 0 0 18 14 14 0 0 0 0-18"/><path d="M3 12h18"/>',
  check: '<rect x="3.5" y="3.5" width="17" height="17" rx="4"/><path d="m8.5 12 2.5 2.5 4.5-5"/>',
  sigma: '<path d="M18 7V4H6l6 8-6 8h12v-3"/>',
  atom: '<circle cx="12" cy="12" r="1"/><path d="M20.2 20.2c2-2 0-7.4-4.5-11.9S5.8 1.8 3.8 3.8s0 7.4 4.5 11.9 9.9 6.5 11.9 4.5Z"/><path d="M15.7 15.7c4.5-4.5 6.5-9.9 4.5-11.9s-7.4 0-11.9 4.5-6.5 9.9-4.5 11.9 7.4 0 11.9-4.5Z"/>',
  flask: '<path d="M9 3h6"/><path d="M10 3v6.5L4.6 18.8A1.5 1.5 0 0 0 5.9 21h12.2a1.5 1.5 0 0 0 1.3-2.2L14 9.5V3"/><path d="M7 15h10"/>',
  leaf: '<path d="M11 20A7 7 0 0 1 9.8 6.1C15.5 5 17 4.5 19 2c1 2 2 4.2 2 8 0 5.5-4.8 10-10 10Z"/><path d="M2 21c0-3 1.9-5.4 5.1-6C9.5 14.5 12 13 13 12"/>',
  code: '<path d="m16 18 6-6-6-6"/><path d="m8 6-6 6 6 6"/>',
  book: '<path d="M2 4h6a4 4 0 0 1 4 4v13a3 3 0 0 0-3-3H2z"/><path d="M22 4h-6a4 4 0 0 0-4 4v13a3 3 0 0 1 3-3h7z"/>',
  chat: '<path d="M7.9 20A9 9 0 1 0 4 16.1L2 22Z"/>',
  palette: '<circle cx="13.5" cy="6.5" r="1"/><circle cx="17.5" cy="10.5" r="1"/><circle cx="8.5" cy="7.5" r="1"/><circle cx="6.5" cy="12.5" r="1"/><path d="M12 2a10 10 0 0 0 0 20c.9 0 1.6-.7 1.6-1.7 0-.4-.2-.8-.4-1.1-.3-.3-.4-.7-.4-1.1 0-.9.7-1.7 1.7-1.7h2A5.5 5.5 0 0 0 22 11c0-5-4.5-9-10-9z"/>',
  music: '<path d="M9 18V5l12-2v13"/><circle cx="6" cy="18" r="3"/><circle cx="18" cy="16" r="3"/>',
  pulse: '<path d="M22 12h-4l-3 9L9 3l-3 9H2"/>',
  chart: '<path d="M3 3v18h18"/><path d="M8 17v-6"/><path d="M13 17V7"/><path d="M18 17v-4"/>',
  bulb: '<path d="M9 18h6"/><path d="M10 22h4"/><path d="M12 2a7 7 0 0 0-4 12.7c.6.5 1 1.3 1 2.3h6c0-1 .4-1.8 1-2.3A7 7 0 0 0 12 2z"/>',
  folder: '<path d="M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.7-.9l-.8-1.2A2 2 0 0 0 7.9 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"/>',
  chevronRight: '<path d="m9 18 6-6-6-6"/>',
  codeXml: '<path d="m18 16 4-4-4-4"/><path d="m6 8-4 4 4 4"/><path d="m14.5 4-5 16"/>',
  crown: '<path d="M2 7l4 10h12l4-10-6 4-4-7-4 7z"/><path d="M6 21h12"/>',
  cap: '<path d="M22 10 12 5 2 10l10 5 10-5z"/><path d="M6 12v5c3 3 9 3 12 0v-5"/>',
  lock: '<rect x="4" y="11" width="16" height="10" rx="2.5"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>',
  checkMark: '<path d="M20 6 9 17l-5-5"/>',
  clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
  users: '<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.9"/><path d="M16 3.1a4 4 0 0 1 0 7.8"/>',
  pencil: '<path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z"/>',
  fileText: '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6"/><path d="M16 13H8"/><path d="M16 17H8"/>',
  close: '<path d="M18 6 6 18"/><path d="m6 6 12 12"/>',
  send: '<path d="M12 19V5"/><path d="m5 12 7-7 7 7"/>',
  spinner: '<path d="M21 12a9 9 0 1 1-6.2-8.6"/>',
  trash: '<path d="M3 6h18"/><path d="M8 6V4h8v2"/><path d="M19 6l-1 14H6L5 6"/>',
};

function icon(name) {
  return `<svg class="ico" viewBox="0 0 24 24" aria-hidden="true">${ICONS[name] || ICONS.folder}</svg>`;
}

// Subject icon mapping based on name keywords
const SUBJECT_ICONS = [
  { keywords: ["история", "истор"], icon: "landmark" },
  { keywords: ["география", "геогр", "всесвит"], icon: "globe" },
  { keywords: ["математик", "матем", "алгебр", "геометр"], icon: "sigma" },
  { keywords: ["физик"], icon: "atom" },
  { keywords: ["химия", "хими"], icon: "flask" },
  { keywords: ["биологи", "биол", "эколог"], icon: "leaf" },
  { keywords: ["программирован", "програм", "информатик", "питон", "python", "js", "java"], icon: "code" },
  { keywords: ["литератур"], icon: "book" },
  { keywords: ["английск", "english", "язык", "deutsch"], icon: "chat" },
  { keywords: ["рисован", "изобраз", "искусств"], icon: "palette" },
  { keywords: ["музык"], icon: "music" },
  { keywords: ["физкультур", "физвоспит", "спорт"], icon: "pulse" },
  { keywords: ["экономик", "эконом"], icon: "chart" },
  { keywords: ["философи"], icon: "bulb" },
  { keywords: ["общее", "общ", "дз", "дедлайн"], icon: "check" },
];

function getSubjectIcon(name) {
  const lower = (name || "").toLowerCase();
  const entry = SUBJECT_ICONS.find((e) => e.keywords.some((k) => lower.includes(k)));
  return icon(entry ? entry.icon : "folder");
}

// Centralized API fetcher with Telegram HMAC-SHA256 initData
async function apiFetch(path, options = {}) {
  const initData = window.Telegram?.WebApp?.initData || "";
  const headers = { ...(options.headers || {}) };
  // FormData (file uploads) sets its own multipart Content-Type with boundary
  if (!(options.body instanceof FormData)) headers["Content-Type"] = "application/json";
  if (initData) headers["Authorization"] = `Bearer ${initData}`;
  const res = await fetch(`${API_BASE}${path}`, { ...options, headers });
  return res;
}

// ============================================================
// THEME
// ============================================================
function initTheme() {
  let saved = null;
  try { saved = localStorage.getItem("app-theme"); } catch {}
  // First launch: follow the Telegram client theme
  const tgScheme = window.Telegram?.WebApp?.colorScheme;
  applyTheme(saved || (tgScheme === "light" ? "light" : "dark"));
}

function applyTheme(theme) {
  document.documentElement.setAttribute("data-theme", theme);
  try { localStorage.setItem("app-theme", theme); } catch {}

  const tg = window.Telegram?.WebApp;
  const bg = theme === "light" ? "#ebebef" : "#000000";
  try {
    if (tg?.setHeaderColor) tg.setHeaderColor(bg);
    if (tg?.setBackgroundColor) tg.setBackgroundColor(bg);
  } catch {}

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
    // Pulling down must refresh the list instead of minimizing the Mini App (Bot API 7.7+)
    try { tg.disableVerticalSwipes?.(); } catch {}
  }

  const fallbackId = tg?.initDataUnsafe?.user?.id || PRIMARY_OWNER_ID;

  // Instantly show the last known profile and subjects while the server responds
  restoreFromCache();

  // All initial requests go in parallel instead of one after another
  const meReq = apiFetch("/admin/me");
  const groupsReq = apiFetch("/admin/groups").catch(() => null);
  const deadlinesReq = apiFetch("/admin/my-deadlines").catch(() => null);

  try {
    const res = await meReq;
    if (res.ok) {
      const data = await res.json();
      currentUser.id = data.telegramId;
      currentUser.username = data.username || data.fullName || "Пользователь";
      currentUser.role = formatRoleName(data.role);
      currentUser.isAuthorized = true;
      writeCache("cache-user", currentUser);
      syncRoleUI();
      await loadGroups(groupsReq, deadlinesReq);
    } else if (res.status === 403) {
      clearCache();
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
    userRoleBadge.innerHTML = `${icon("lock")}Ограничен`;
    userRoleBadge.className = "role-badge user";
  }
  if (headerUsername) headerUsername.textContent = "Доступ не подтвержден";
}

if (copyMyIdBtn) {
  copyMyIdBtn.addEventListener("click", () => {
    const id = deniedTelegramId?.textContent;
    if (id) {
      navigator.clipboard.writeText(id).then(() => {
        copyMyIdBtn.textContent = "Скопировано";
        setTimeout(() => { copyMyIdBtn.textContent = "Скопировать ID"; }, 2000);
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
  let roleText = `${icon("cap")}Студент`;
  let roleClass = "user";
  if (currentUser.role === "Developer") {
    roleText = `${icon("codeXml")}Разработчик`;
    roleClass = "dev";
  } else if (currentUser.role === "Admin") {
    roleText = `${icon("crown")}Админ`;
    roleClass = "admin";
  }

  if (userRoleBadge) {
    userRoleBadge.innerHTML = roleText;
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
  } else if (screenId === "deadlinesScreen") {
    headerTitle.textContent = "Дедлайны";
    loadDeadlinesScreen();
  } else if (screenId === "usersScreen") {
    headerTitle.textContent = "Группа";
    if (currentUsersTab === "stats") loadStats(); else loadUsers();
  } else if (screenId === "settingsScreen") {
    headerTitle.textContent = "Настройки";
  }

  syncRoleUI();
  updateTelegramBackButton();
  // Animated refresh: running slides continue, indicators that were hidden snap into place
  requestAnimationFrame(() => refreshIndicators(true));
}

// Where "Back" from a subject leads: the subjects list or the deadlines screen it was opened from
let chatReturnScreen = "groupsScreen";

backBtn.addEventListener("click", () => {
  switchScreen(chatReturnScreen);
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

// ============================================================
// LOCAL CACHE (instant first paint, refreshed from the server)
// ============================================================
function readCache(key) {
  try { return JSON.parse(localStorage.getItem(key) || "null"); } catch { return null; }
}

function writeCache(key, value) {
  try { localStorage.setItem(key, JSON.stringify(value)); } catch {}
}

function clearCache() {
  try { ["cache-user", "cache-groups"].forEach((k) => localStorage.removeItem(k)); } catch {}
}

function restoreFromCache() {
  const user = readCache("cache-user");
  const groups = readCache("cache-groups");
  if (!user || !Array.isArray(groups)) return;
  Object.assign(currentUser, user);
  syncRoleUI();
  cachedGroups = groups;
  renderGroupsList(cachedGroups);
}

async function loadGroups(prefetchedGroups = null, prefetchedDeadlines = null) {
  // Keep showing cached subjects instead of a loader while refreshing
  if (cachedGroups.length === 0) {
    groupsList.innerHTML = skeleton("group", 3);
  }
  try {
    const res = prefetchedGroups ? await prefetchedGroups : await apiFetch("/admin/groups");
    if (!res || !res.ok) throw new Error("Не удалось загрузить группы");
    cachedGroups = await res.json();
    writeCache("cache-groups", cachedGroups);
    renderGroupsList(cachedGroups);
    await loadDeadlinesSummary(prefetchedDeadlines);
  } catch (err) {
    console.error(err);
    if (cachedGroups.length === 0) {
      groupsList.innerHTML = '<div class="empty-state">Не удалось загрузить предметы. Проверьте соединение.</div>';
    }
  }
}

async function loadDeadlinesSummary(prefetched = null) {
  if (!deadlinesSummaryCard) return;
  try {
    const res = prefetched ? await prefetched : await apiFetch("/admin/my-deadlines");
    if (!res) return;
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
      deadlinesSummaryTitle.textContent = `Горящие дедлайны · ${urgent.length}`;
      const subjects = [...new Set(urgent.map((u) => u.groupName))].join(", ");
      deadlinesSummaryText.textContent = `Срочно сдать задания по: ${subjects}`;
      deadlinesSummaryCard.classList.add("urgent");
    } else {
      deadlinesSummaryTitle.textContent = `Ближайшие задания · ${uncompleted.length}`;
      deadlinesSummaryText.textContent = `Выполнено ${items.length - uncompleted.length} из ${items.length} дедлайнов`;
      deadlinesSummaryCard.classList.remove("urgent");
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

    const subjectIcon = getSubjectIcon(g.name);
    // Message count badge (only if > 0)
    const countBadgeHtml = g.messageCount > 0
      ? `<span class="group-count-badge">${g.messageCount}</span>`
      : "";

    item.innerHTML = `
      <div class="group-avatar">${subjectIcon}</div>
      <div class="group-info">
        <h3>${escapeHtml(g.name)}</h3>
        <p>${escapeHtml(g.description || "Задания и дедлайны")}</p>
      </div>
      <div class="group-right">
        ${countBadgeHtml}
        <span class="arrow-icon">${icon("chevronRight")}</span>
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
/**
 * Opens a subject. options.returnTo — screen for the Back button,
 * options.highlightId — task to scroll to and highlight.
 */
async function openChat(groupId, name, desc, options = {}) {
  chatReturnScreen = options.returnTo || "groupsScreen";
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

  resetComposer();

  syncRoleUI();
  updateTelegramBackButton();
  // Animated refresh: running slides continue, indicators that were hidden snap into place
  requestAnimationFrame(() => refreshIndicators(true));
  await loadMessages(groupId, false, options.highlightId ? { flash: [options.highlightId] } : {});
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
async function loadMessages(groupId, silent = false, animate = {}) {
  // silent: refresh in place (pull-to-refresh, after saving) without flashing a loader
  if (!silent) messagesContainer.innerHTML = skeleton("message", 2);
  try {
    const res = await apiFetch(`/admin/group/${groupId}/messages`);
    cachedMessages = await res.json();
    updateTaskProgress();
    renderFilteredMessages(animate);
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
function filteredMessages() {
  if (currentTaskFilter === "pending") return cachedMessages.filter((m) => !m.isCompleted);
  if (currentTaskFilter === "completed") return cachedMessages.filter((m) => m.isCompleted);
  return cachedMessages;
}

function emptyMessagesHtml() {
  let text = "Пока нет записей. Администратор ещё не опубликовал ДЗ.";
  if (currentTaskFilter === "pending") text = "Все задания по этому предмету выполнены.";
  if (currentTaskFilter === "completed") text = "Пока нет выполненных заданий.";
  return `<div class="empty-state fade-in">${text}</div>`;
}

/**
 * Renders the task list.
 * animate.enter — ids of new tasks (slide in), animate.flash — ids of edited tasks (highlight).
 */
function renderFilteredMessages(animate = {}) {
  const list = filteredMessages();
  if (list.length === 0) {
    messagesContainer.innerHTML = emptyMessagesHtml();
    return;
  }

  // Markdown: line breaks and GitHub syntax
  if (typeof marked !== "undefined") marked.setOptions({ breaks: true, gfm: true });

  messagesContainer.innerHTML = "";
  list.forEach((m) => {
    const card = createMessageCard(m);
    if (animate.enter?.includes(m.id)) card.classList.add("card-enter");
    if (animate.flash?.includes(m.id)) card.classList.add("card-flash");
    messagesContainer.appendChild(card);
  });

  const highlighted = messagesContainer.querySelector(".card-enter, .card-flash");
  highlighted?.scrollIntoView({ behavior: "smooth", block: "center" });
}

function toggleButtonHtml(isCompleted) {
  return isCompleted
    ? `<svg class="ico check-draw" viewBox="0 0 24 24"><path d="M20 6 9 17l-5-5"/></svg>Сдано`
    : "Отметить сданным";
}

function createMessageCard(m) {
  const canManage = currentUser.role === "Developer" || currentUser.role === "Admin";
  const card = document.createElement("div");
  card.className = `message-card ${m.isCompleted ? "completed" : ""}`;
  card.dataset.id = m.id;

  const date = new Date(m.sentAt);
  const dateFormatted = date.toLocaleDateString("ru-RU", { day: "2-digit", month: "short" });
  const timeFormatted = date.toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" });

  // Markdown is rendered only together with DOMPurify, otherwise HTML in the text would execute (XSS)
  const textHtml = typeof marked !== "undefined" && typeof DOMPurify !== "undefined"
    ? DOMPurify.sanitize(marked.parse(m.text || ""))
    : escapeHtml(m.text).replace(/\n/g, "<br>");

  card.innerHTML = `
    <div class="message-card-header">
      <span class="message-author">${escapeHtml(m.author)}</span>
      <div class="message-meta">
        <span class="message-date">${dateFormatted}, ${timeFormatted}${m.editedAt ? " · изменено" : ""}</span>
        ${canManage ? `
          <button class="msg-action-btn edit-msg-btn" title="Редактировать">${icon("pencil")}</button>
          <button class="msg-action-btn delete-msg-btn" title="Удалить запись">${icon("close")}</button>` : ""}
      </div>
    </div>
    <div class="message-text">${textHtml}</div>
    ${renderAttachments(m.attachments)}
    <div class="message-footer-row">
      <div class="footer-left">
        <div class="deadline-slot">${formatDeadlineBadge(m.deadline, m.isCompleted)}</div>
        ${m.completedCount != null ? `<button type="button" class="done-count-chip" title="Кто сдал">${icon("users")}<span>${m.completedCount}/${m.studentsTotal}</span></button>` : ""}
      </div>
      <button class="task-toggle-btn ${m.isCompleted ? "completed" : ""}">${toggleButtonHtml(m.isCompleted)}</button>
    </div>
  `;

  card.querySelector(".task-toggle-btn").addEventListener("click", () => toggleTaskComplete(m.id));
  card.querySelector(".done-count-chip")?.addEventListener("click", () => openTaskProgressSheet(m.id));
  if (canManage) {
    card.querySelector(".delete-msg-btn")?.addEventListener("click", () => deleteMessage(m.id));
    card.querySelector(".edit-msg-btn")?.addEventListener("click", () => startEdit(m));
  }
  bindAttachmentHandlers(card);
  return card;
}

const prefersReducedMotion = () => window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;

/** Smoothly collapses an element to zero height, then removes it. */
function collapseAndRemove(el) {
  return new Promise((resolve) => {
    if (prefersReducedMotion()) {
      el.remove();
      resolve();
      return;
    }
    el.style.height = `${el.offsetHeight}px`;
    el.getBoundingClientRect(); // fix the start height before the transition
    el.classList.add("card-leave");
    el.style.height = "0px";
    setTimeout(() => {
      el.remove();
      resolve();
    }, 320);
  });
}

// Toggle Task Complete — the card is updated in place so the change is animated
async function toggleTaskComplete(msgId) {
  const haptic = window.Telegram?.WebApp?.HapticFeedback;
  haptic?.impactOccurred("medium");

  const card = messagesContainer.querySelector(`.message-card[data-id="${msgId}"]`);
  const button = card?.querySelector(".task-toggle-btn");
  if (button) button.disabled = true;

  try {
    const res = await apiFetch(`/admin/message/${msgId}/toggle-complete`, { method: "POST" });
    if (!res.ok) {
      await handleResponseError(res, "Не удалось изменить отметку");
      return;
    }
    const data = await res.json();
    const msg = cachedMessages.find((m) => m.id === msgId);
    if (!msg) return;
    msg.isCompleted = data.isCompleted;
    msg.completedAt = data.completedAt;
    if (msg.isCompleted) haptic?.notificationOccurred("success");

    updateTaskProgress();
    loadDeadlinesSummary();

    const stillVisible = filteredMessages().some((m) => m.id === msgId);
    if (card && stillVisible) {
      card.classList.toggle("completed", msg.isCompleted);
      button.classList.toggle("completed", msg.isCompleted);
      button.innerHTML = toggleButtonHtml(msg.isCompleted);
      button.classList.remove("just-done");
      if (msg.isCompleted) {
        button.getBoundingClientRect();
        button.classList.add("just-done");
      }
      card.querySelector(".deadline-slot").innerHTML = formatDeadlineBadge(msg.deadline, msg.isCompleted);
      if (msg.completedCount != null && currentUser.role === "Admin") {
        msg.completedCount += msg.isCompleted ? 1 : -1;
        const chip = card.querySelector(".done-count-chip span");
        if (chip) chip.textContent = `${msg.completedCount}/${msg.studentsTotal}`;
      }
    } else if (card) {
      // The task no longer matches the filter ("Не сдано" / "Сдано") — fold it away
      await collapseAndRemove(card);
      if (filteredMessages().length === 0) messagesContainer.innerHTML = emptyMessagesHtml();
    } else {
      renderFilteredMessages();
    }
  } catch (err) {
    console.error("Toggle complete error:", err);
  } finally {
    if (button) button.disabled = false;
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

  if (isCompleted) return `<span class="deadline-badge done">${icon("checkMark")}${formattedDate}</span>`;
  if (diffHours < 0) return `<span class="deadline-badge overdue">${icon("clock")}просрочено</span>`;
  if (diffHours < 24) {
    const hoursLeft = Math.max(1, Math.round(diffHours));
    return `<span class="deadline-badge urgent">${icon("clock")}${formattedDate} · ${hoursLeft} ч</span>`;
  }
  return `<span class="deadline-badge normal">${icon("clock")}${formattedDate} · ${diffDays} дн</span>`;
}

// ============================================================
// ATTACHMENTS (photos, PDF) — viewing
// ============================================================
function formatFileSize(bytes) {
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} КБ`;
  return `${(bytes / 1024 / 1024).toFixed(1).replace(".", ",")} МБ`;
}

function renderAttachments(attachments) {
  if (!attachments || attachments.length === 0) return "";
  const images = attachments.filter((a) => a.isImage);
  const docs = attachments.filter((a) => !a.isImage);

  const imagesHtml = images.length
    ? `<div class="attach-grid count-${Math.min(images.length, 3)}">${images.map((a) => `
        <button type="button" class="attach-photo" data-photo="${escapeHtml(a.url)}">
          <img src="${escapeHtml(a.url)}" alt="${escapeHtml(a.fileName)}" loading="lazy" />
        </button>`).join("")}</div>`
    : "";

  const docsHtml = docs.map((a) => `
    <button type="button" class="attach-doc" data-doc="${escapeHtml(a.url)}">
      <span class="attach-doc-icon">${icon("fileText")}</span>
      <span class="attach-doc-info">
        <span class="attach-doc-name">${escapeHtml(a.fileName)}</span>
        <span class="attach-doc-size">PDF · ${formatFileSize(a.size)}</span>
      </span>
    </button>`).join("");

  return `<div class="message-attachments">${imagesHtml}${docsHtml}</div>`;
}

function bindAttachmentHandlers(root) {
  root.querySelectorAll("[data-photo]").forEach((el) =>
    el.addEventListener("click", () => openLightbox(el.dataset.photo)));
  root.querySelectorAll("[data-doc]").forEach((el) =>
    el.addEventListener("click", () => openDocument(el.dataset.doc)));
}

function openDocument(url) {
  const absolute = new URL(url, window.location.origin).toString();
  const tg = window.Telegram?.WebApp;
  // Inside Telegram a PDF opens in the system viewer/browser; elsewhere in a new tab
  if (tg?.openLink && tg.initData) tg.openLink(absolute);
  else window.open(absolute, "_blank", "noopener");
}

const lightbox = document.getElementById("lightbox");
const lightboxImg = document.getElementById("lightboxImg");

function openLightbox(url) {
  lightboxImg.src = url;
  lightbox.classList.remove("hidden");
  updateTelegramBackButton();
}

function closeLightbox() {
  lightbox.classList.add("hidden");
  lightboxImg.removeAttribute("src");
  updateTelegramBackButton();
}

lightbox.addEventListener("click", closeLightbox);

// ============================================================
// COMPOSER — publish / edit with files
// ============================================================
const MAX_FILES = 5;
const MAX_FILE_SIZE = 20 * 1024 * 1024;
const composerFilesEl = document.getElementById("composerFiles");
const fileInput = document.getElementById("fileInput");
const attachBtn = document.getElementById("attachBtn");
const editBanner = document.getElementById("editBanner");
const cancelEditBtn = document.getElementById("cancelEditBtn");

let composer = {
  editingId: null,   // id of the task being edited, null = new task
  newFiles: [],      // File objects to upload: { file, previewUrl }
  existing: [],      // attachments already saved (edit mode)
  removeIds: []      // saved attachments to delete on save
};

function resetComposer() {
  composer.newFiles.forEach((f) => f.previewUrl && URL.revokeObjectURL(f.previewUrl));
  composer = { editingId: null, newFiles: [], existing: [], removeIds: [] };
  adminMessageInput.value = "";
  if (adminDeadlineInput) adminDeadlineInput.value = "";
  if (deadlineContainer) deadlineContainer.classList.add("hidden");
  editBanner.classList.add("hidden");
  sendMessageBtn.innerHTML = `<svg class="ico" viewBox="0 0 24 24">${ICONS.send}</svg>`;
  sendMessageBtn.title = "Опубликовать ДЗ";
  updateDeadlinePreview();
  renderComposerFiles();
}

function startEdit(m) {
  resetComposer();
  composer.editingId = m.id;
  composer.existing = (m.attachments || []).slice();
  adminMessageInput.value = m.text || "";
  if (m.deadline && adminDeadlineInput) adminDeadlineInput.value = formatInputDateTime(new Date(m.deadline));
  editBanner.classList.remove("hidden");
  sendMessageBtn.innerHTML = `<svg class="ico" viewBox="0 0 24 24">${ICONS.checkMark}</svg>`;
  sendMessageBtn.title = "Сохранить изменения";
  updateDeadlinePreview();
  renderComposerFiles();
  adminMessageInput.focus();
  updateTelegramBackButton();
}

cancelEditBtn.addEventListener("click", () => {
  resetComposer();
  updateTelegramBackButton();
});

function attachmentsCount() {
  return composer.existing.length + composer.newFiles.length;
}

function renderComposerFiles() {
  const items = [
    ...composer.existing.map((a) => ({
      key: `e${a.id}`, name: a.fileName, isImage: a.isImage, preview: a.isImage ? a.url : null
    })),
    ...composer.newFiles.map((f, i) => ({
      key: `n${i}`, name: f.file.name, isImage: f.file.type.startsWith("image/"), preview: f.previewUrl
    }))
  ];

  composerFilesEl.classList.toggle("hidden", items.length === 0);
  composerFilesEl.innerHTML = items.map((it) => `
    <div class="composer-chip">
      ${it.isImage && it.preview
        ? `<img src="${escapeHtml(it.preview)}" alt="" />`
        : `<span class="composer-chip-icon">${icon("fileText")}</span>`}
      <span class="composer-chip-name">${escapeHtml(it.name)}</span>
      <button type="button" class="composer-chip-remove" data-remove="${it.key}" title="Убрать">${icon("close")}</button>
    </div>`).join("");

  composerFilesEl.querySelectorAll("[data-remove]").forEach((btn) => {
    btn.addEventListener("click", () => {
      const key = btn.dataset.remove;
      if (key.startsWith("e")) {
        const id = Number(key.slice(1));
        composer.existing = composer.existing.filter((a) => a.id !== id);
        composer.removeIds.push(id);
      } else {
        const [removed] = composer.newFiles.splice(Number(key.slice(1)), 1);
        if (removed?.previewUrl) URL.revokeObjectURL(removed.previewUrl);
      }
      renderComposerFiles();
    });
  });
}

attachBtn.addEventListener("click", () => {
  if (attachmentsCount() >= MAX_FILES) {
    alert(`Можно прикрепить не больше ${MAX_FILES} файлов.`);
    return;
  }
  fileInput.click();
});

fileInput.addEventListener("change", async () => {
  const picked = Array.from(fileInput.files || []);
  fileInput.value = "";
  for (const original of picked) {
    if (attachmentsCount() >= MAX_FILES) {
      alert(`Можно прикрепить не больше ${MAX_FILES} файлов.`);
      break;
    }
    const isPdf = original.type === "application/pdf";
    const isImage = /^image\/(jpeg|png|webp|gif)$/.test(original.type);
    if (!isPdf && !isImage) {
      alert(`«${original.name}»: можно прикреплять только фото (JPG, PNG, WEBP, GIF) и PDF.`);
      continue;
    }
    const file = isImage ? await compressImage(original) : original;
    if (file.size > MAX_FILE_SIZE) {
      alert(`«${original.name}» больше 20 МБ.`);
      continue;
    }
    composer.newFiles.push({ file, previewUrl: isImage ? URL.createObjectURL(file) : null });
  }
  renderComposerFiles();
});

/**
 * Shrinks large photos before upload (max 2048 px, JPEG 85%):
 * a phone photo goes from ~4 MB to ~400 KB — faster upload and faster loading for students.
 */
async function compressImage(file) {
  if (file.type === "image/gif" || file.size < 600 * 1024) return file;
  try {
    const bitmap = await createImageBitmap(file);
    const scale = Math.min(1, 2048 / Math.max(bitmap.width, bitmap.height));
    const canvas = document.createElement("canvas");
    canvas.width = Math.round(bitmap.width * scale);
    canvas.height = Math.round(bitmap.height * scale);
    canvas.getContext("2d").drawImage(bitmap, 0, 0, canvas.width, canvas.height);
    bitmap.close?.();
    const blob = await new Promise((resolve) => canvas.toBlob(resolve, "image/jpeg", 0.85));
    if (!blob || blob.size >= file.size) return file;
    const name = file.name.replace(/\.[^.]+$/, "") + ".jpg";
    return new File([blob], name, { type: "image/jpeg" });
  } catch {
    return file; // the browser cannot decode it — upload as is
  }
}

// Publish a new task or save the edited one
sendMessageBtn.addEventListener("click", async () => {
  const text = adminMessageInput.value.trim();
  if (!text || !currentGroupId) return;

  const form = new FormData();
  form.append("text", text);
  form.append("deadline", adminDeadlineInput?.value ? new Date(adminDeadlineInput.value).toISOString() : "");
  composer.newFiles.forEach((f) => form.append("files", f.file, f.file.name));

  const editing = composer.editingId !== null;
  if (editing) composer.removeIds.forEach((id) => form.append("removeAttachmentIds", String(id)));

  const path = editing ? `/admin/message/${composer.editingId}` : `/admin/group/${currentGroupId}/message`;
  const hasFiles = composer.newFiles.length > 0;

  const previousIcon = sendMessageBtn.innerHTML;
  let saved = false;
  sendMessageBtn.disabled = true;
  sendMessageBtn.classList.add("loading");
  sendMessageBtn.innerHTML = `<svg class="ico" viewBox="0 0 24 24">${ICONS.spinner}</svg>`;
  try {
    const res = await apiFetch(path, { method: editing ? "PATCH" : "POST", body: form });
    if (res.ok) {
      saved = true;
      const { id } = await res.json().catch(() => ({}));
      window.Telegram?.WebApp?.HapticFeedback?.notificationOccurred("success");
      resetComposer();
      updateTelegramBackButton();
      await loadMessages(currentGroupId, true, editing ? { flash: [id] } : { enter: [id] });
      await loadDeadlinesSummary();
    } else {
      await handleResponseError(res, editing ? "Не удалось сохранить изменения" : "Не удалось опубликовать задание");
    }
  } catch (e) {
    console.error(e);
    alert(hasFiles ? "Ошибка соединения при загрузке файлов. Попробуйте ещё раз." : "Ошибка соединения");
  } finally {
    sendMessageBtn.disabled = false;
    sendMessageBtn.classList.remove("loading");
    if (!saved) sendMessageBtn.innerHTML = previousIcon; // on success resetComposer() already set the icon
  }
});

// Delete Message
async function deleteMessage(id) {
  if (!confirm("Удалить это задание?")) return;
  try {
    const res = await apiFetch(`/admin/message/${id}`, { method: "DELETE" });
    if (res.ok) {
      cachedMessages = cachedMessages.filter((m) => m.id !== id);
      updateTaskProgress();
      loadDeadlinesSummary();
      const card = messagesContainer.querySelector(`.message-card[data-id="${id}"]`);
      if (card) await collapseAndRemove(card);
      if (filteredMessages().length === 0) messagesContainer.innerHTML = emptyMessagesHtml();
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
  usersList.innerHTML = skeleton("user", 3);
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
          <button class="delete-user-btn" data-del-user="${u.telegramId}" title="Удалить из системы">${icon("trash")}</button>
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
// MY DEADLINES SCREEN — tasks with a deadline from all subjects
// ============================================================
const deadlinesScreen = document.getElementById("deadlinesScreen");
const deadlinesList = document.getElementById("deadlinesList");
const deadlineFilterTabs = document.querySelectorAll(".deadline-filter-tab");
const countActiveDeadlines = document.getElementById("countActiveDeadlines");
const countDoneDeadlines = document.getElementById("countDoneDeadlines");

let cachedDeadlines = [];
let currentDeadlineFilter = "active"; // "active" | "done"

deadlineFilterTabs.forEach((tab) => {
  tab.addEventListener("click", () => {
    deadlineFilterTabs.forEach((t) => t.classList.toggle("active", t === tab));
    currentDeadlineFilter = tab.dataset.deadlineFilter;
    renderDeadlines();
  });
});

// The summary banner on the subjects screen opens the full list
deadlinesSummaryCard?.addEventListener("click", () => switchScreen("deadlinesScreen"));

async function loadDeadlinesScreen(silent = false) {
  if (!silent || cachedDeadlines.length === 0) deadlinesList.innerHTML = skeleton("deadline", 4);
  try {
    const res = await apiFetch("/admin/my-deadlines");
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    cachedDeadlines = await res.json();
    renderDeadlines();
  } catch (err) {
    console.error(err);
    deadlinesList.innerHTML = '<div class="empty-state">Не удалось загрузить дедлайны. Потяните вниз, чтобы повторить.</div>';
  }
}

const startOfDay = (d) => new Date(d.getFullYear(), d.getMonth(), d.getDate());
const DAY_MS = 24 * 60 * 60 * 1000;

/** Section of an active task: overdue, today, tomorrow, this week, later. */
function deadlineBucket(deadline, now) {
  if (deadline < now) return "overdue";
  const days = Math.round((startOfDay(deadline) - startOfDay(now)) / DAY_MS);
  if (days === 0) return "today";
  if (days === 1) return "tomorrow";
  if (days < 7) return "week";
  return "later";
}

const BUCKET_TITLES = {
  overdue: "Просрочено",
  today: "Сегодня",
  tomorrow: "Завтра",
  week: "На этой неделе",
  later: "Позже",
  done: "Сданные"
};

/** Markdown → short plain text for the list preview. */
function plainPreview(text) {
  return (text || "")
    .replace(/```[\s\S]*?```/g, " ")
    .replace(/!?\[([^\]]*)\]\([^)]*\)/g, "$1")
    .replace(/[*_`#>~]/g, "")
    .replace(/\s+/g, " ")
    .trim();
}

function renderDeadlines() {
  const now = new Date();
  const active = cachedDeadlines.filter((d) => !d.isCompleted);
  const done = cachedDeadlines.filter((d) => d.isCompleted);
  countActiveDeadlines.textContent = active.length || "";
  countDoneDeadlines.textContent = done.length || "";

  const items = currentDeadlineFilter === "done"
    ? done.slice().sort((a, b) => new Date(b.deadline) - new Date(a.deadline))
    : active;

  if (items.length === 0) {
    deadlinesList.innerHTML = currentDeadlineFilter === "done"
      ? '<div class="empty-state fade-in">Пока нет сданных заданий с дедлайном.</div>'
      : `<div class="deadlines-empty fade-in">
           <div class="deadlines-empty-icon">${icon("checkMark")}</div>
           <div class="deadlines-empty-title">Всё сдано</div>
           <div class="deadlines-empty-text">Активных дедлайнов нет. Новые появятся здесь автоматически.</div>
         </div>`;
    return;
  }

  // Group into sections, keeping the API order (sorted by deadline)
  const sections = new Map();
  items.forEach((d) => {
    const key = currentDeadlineFilter === "done" ? "done" : deadlineBucket(new Date(d.deadline), now);
    if (!sections.has(key)) sections.set(key, []);
    sections.get(key).push(d);
  });

  deadlinesList.innerHTML = "";
  ["overdue", "today", "tomorrow", "week", "later", "done"].forEach((key) => {
    const group = sections.get(key);
    if (!group) return;
    const section = document.createElement("section");
    section.className = `deadline-section ${key}`;
    section.innerHTML = `
      <div class="deadline-section-title"><span>${BUCKET_TITLES[key]}</span><span>${group.length}</span></div>
      <div class="deadline-section-items"></div>`;
    const box = section.querySelector(".deadline-section-items");
    group.forEach((d) => box.appendChild(createDeadlineItem(d, now)));
    deadlinesList.appendChild(section);
  });
}

function deadlineTimeLabels(deadline, now, isCompleted) {
  const sameDayOrNext = ["today", "tomorrow"].includes(deadlineBucket(deadline, now)) && deadline >= now;
  const main = sameDayOrNext
    ? deadline.toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" })
    : deadline.toLocaleDateString("ru-RU", { day: "numeric", month: "short" });

  if (isCompleted) return { main, sub: "сдано", tone: "done" };
  const hours = (deadline - now) / (60 * 60 * 1000);
  if (hours < 0) {
    const daysLate = Math.floor(-hours / 24);
    return { main, sub: daysLate >= 1 ? `${daysLate} дн назад` : "просрочено", tone: "overdue" };
  }
  if (hours < 24) return { main, sub: `через ${Math.max(1, Math.round(hours))} ч`, tone: "urgent" };
  // Count calendar days, not 24-hour periods: "tomorrow 23:00" is "завтра", not "через 2 дн"
  const days = Math.round((startOfDay(deadline) - startOfDay(now)) / DAY_MS);
  return { main, sub: days === 1 ? "завтра" : `через ${days} дн`, tone: "normal" };
}

function createDeadlineItem(d, now) {
  const deadline = new Date(d.deadline);
  const time = deadlineTimeLabels(deadline, now, d.isCompleted);

  const item = document.createElement("div");
  item.className = `deadline-item ${d.isCompleted ? "completed" : ""}`;
  item.dataset.id = d.id;
  item.innerHTML = `
    <button type="button" class="deadline-check ${d.isCompleted ? "checked" : ""}" title="${d.isCompleted ? "Отменить «Сдано»" : "Отметить сданным"}">
      <svg class="ico" viewBox="0 0 24 24"><path d="M20 6 9 17l-5-5"/></svg>
    </button>
    <div class="deadline-item-body">
      <div class="deadline-item-subject">${getSubjectIcon(d.groupName)}<span>${escapeHtml(d.groupName)}</span></div>
      <div class="deadline-item-text">${escapeHtml(plainPreview(d.text)) || "Без описания"}</div>
    </div>
    <div class="deadline-item-time ${time.tone}">
      <span class="deadline-item-main">${time.main}</span>
      <span class="deadline-item-sub">${time.sub}</span>
    </div>`;

  item.querySelector(".deadline-check").addEventListener("click", (e) => {
    e.stopPropagation();
    toggleDeadlineItem(d, item);
  });
  item.addEventListener("click", () => {
    const group = cachedGroups.find((g) => g.id === d.chatGroupId);
    openChat(d.chatGroupId, d.groupName, group?.description, { returnTo: "deadlinesScreen", highlightId: d.id });
  });
  return item;
}

async function toggleDeadlineItem(d, item) {
  const haptic = window.Telegram?.WebApp?.HapticFeedback;
  haptic?.impactOccurred("medium");
  const check = item.querySelector(".deadline-check");
  check.disabled = true;
  try {
    const res = await apiFetch(`/admin/message/${d.id}/toggle-complete`, { method: "POST" });
    if (!res.ok) {
      await handleResponseError(res, "Не удалось изменить отметку");
      return;
    }
    const data = await res.json();
    d.isCompleted = data.isCompleted;
    d.completedAt = data.completedAt;
    if (d.isCompleted) haptic?.notificationOccurred("success");

    // Show the check first, then fold the item away from the current tab
    check.classList.toggle("checked", d.isCompleted);
    item.classList.toggle("completed", d.isCompleted);
    await new Promise((r) => setTimeout(r, prefersReducedMotion() ? 0 : 280));

    const section = item.closest(".deadline-section");
    await collapseAndRemove(item);
    if (section && !section.querySelector(".deadline-item")) await collapseAndRemove(section);
    else if (section) section.querySelector(".deadline-section-title span:last-child").textContent =
      section.querySelectorAll(".deadline-item").length;

    const active = cachedDeadlines.filter((x) => !x.isCompleted).length;
    countActiveDeadlines.textContent = active || "";
    countDoneDeadlines.textContent = (cachedDeadlines.length - active) || "";
    if (!deadlinesList.querySelector(".deadline-item")) renderDeadlines();
    loadDeadlinesSummary();
  } catch (err) {
    console.error(err);
  } finally {
    check.disabled = false;
  }
}

// ============================================================
// BOTTOM SHEET
// ============================================================
const sheet = document.getElementById("sheet");
const sheetTitle = document.getElementById("sheetTitle");
const sheetSubtitle = document.getElementById("sheetSubtitle");
const sheetBody = document.getElementById("sheetBody");

function openSheet(title, subtitle, bodyHtml) {
  sheetTitle.textContent = title;
  sheetSubtitle.textContent = subtitle || "";
  sheetBody.innerHTML = bodyHtml;
  sheetBody.scrollTop = 0;
  sheet.classList.remove("hidden", "closing");
  updateTelegramBackButton();
}

function closeSheet() {
  if (sheet.classList.contains("hidden")) return;
  sheet.classList.add("closing");
  setTimeout(() => {
    sheet.classList.add("hidden");
    sheet.classList.remove("closing");
    updateTelegramBackButton();
  }, prefersReducedMotion() ? 0 : 200);
}

document.getElementById("sheetClose").addEventListener("click", closeSheet);
sheet.addEventListener("click", (e) => { if (e.target === sheet) closeSheet(); });

// ============================================================
// STATISTICS (monitor & developers): Группа → Статистика
// ============================================================
const statsPane = document.getElementById("statsPane");
const membersPane = document.getElementById("membersPane");
const statsTotals = document.getElementById("statsTotals");
const statsList = document.getElementById("statsList");
const usersTabs = document.querySelectorAll(".users-tab");
const statsViewTabs = document.querySelectorAll(".stats-view-tab");

let currentUsersTab = "members";   // "members" | "stats"
let currentStatsView = "tasks";    // "tasks" | "students"
let cachedStats = null;

usersTabs.forEach((tab) => {
  tab.addEventListener("click", () => {
    usersTabs.forEach((t) => t.classList.toggle("active", t === tab));
    currentUsersTab = tab.dataset.usersTab;
    const isStats = currentUsersTab === "stats";
    statsPane.classList.toggle("hidden", !isStats);
    membersPane.classList.toggle("hidden", isStats);
    if (isStats) loadStats(); else loadUsers();
    requestAnimationFrame(() => refreshIndicators(true));
  });
});

statsViewTabs.forEach((tab) => {
  tab.addEventListener("click", () => {
    statsViewTabs.forEach((t) => t.classList.toggle("active", t === tab));
    currentStatsView = tab.dataset.statsView;
    renderStatsList();
  });
});

async function loadStats(silent = false) {
  if (!silent || !cachedStats) {
    statsTotals.innerHTML = `<div class="stat-tile skel"></div>`.repeat(3);
    statsList.innerHTML = skeleton("deadline", 4);
  }
  try {
    const res = await apiFetch("/admin/stats");
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    cachedStats = await res.json();
    renderStats();
  } catch (err) {
    console.error(err);
    statsTotals.innerHTML = "";
    statsList.innerHTML = '<div class="empty-state">Не удалось загрузить статистику. Потяните вниз, чтобы повторить.</div>';
  }
}

function renderStats() {
  const t = cachedStats.totals;
  statsTotals.innerHTML = `
    <div class="stat-tile"><span class="stat-value">${t.students}</span><span class="stat-label">студентов</span></div>
    <div class="stat-tile"><span class="stat-value">${t.completionPercent}%</span><span class="stat-label">сдано</span></div>
    <div class="stat-tile ${t.overdueCount ? "bad" : ""}"><span class="stat-value">${t.overdueCount}</span><span class="stat-label">просрочек</span></div>`;
  renderStatsList();
}

function progressBar(done, total) {
  const percent = total ? Math.round((done / total) * 100) : 0;
  const tone = percent === 100 ? "full" : percent < 50 ? "low" : "";
  return `<div class="mini-progress ${tone}"><div style="width:${percent}%"></div></div>`;
}

function renderStatsList() {
  if (!cachedStats) return;
  const now = new Date();
  const rows = document.createElement("div");
  rows.className = "deadline-section-items fade-in";

  if (currentStatsView === "tasks") {
    if (cachedStats.tasks.length === 0) {
      statsList.innerHTML = '<div class="empty-state">Заданий пока нет.</div>';
      return;
    }
    cachedStats.tasks.forEach((task) => {
      const deadline = task.deadline ? new Date(task.deadline) : null;
      const overdue = deadline && deadline < now && task.completed < task.total;
      const row = document.createElement("button");
      row.type = "button";
      row.className = "stats-row";
      row.innerHTML = `
        <div class="stats-row-body">
          <div class="deadline-item-subject">${getSubjectIcon(task.groupName)}<span>${escapeHtml(task.groupName)}</span></div>
          <div class="stats-row-title">${escapeHtml(plainPreview(task.text)) || "Без описания"}</div>
          ${progressBar(task.completed, task.total)}
        </div>
        <div class="stats-row-side">
          <span class="stats-row-count">${task.completed}/${task.total}</span>
          <span class="stats-row-sub ${overdue ? "bad" : ""}">${deadline
            ? (overdue ? "просрочено" : deadline.toLocaleDateString("ru-RU", { day: "numeric", month: "short" }))
            : "без срока"}</span>
        </div>`;
      row.addEventListener("click", () => openTaskProgressSheet(task.id));
      rows.appendChild(row);
    });
  } else {
    if (cachedStats.students.length === 0) {
      statsList.innerHTML = '<div class="empty-state">Студенты ещё не заходили в приложение.</div>';
      return;
    }
    cachedStats.students.forEach((st) => {
      const row = document.createElement("button");
      row.type = "button";
      row.className = "stats-row";
      row.innerHTML = `
        <div class="user-avatar small">${escapeHtml((st.name || "?")[0].toUpperCase())}</div>
        <div class="stats-row-body">
          <div class="stats-row-title">${escapeHtml(st.name)}${formatRoleName(st.role) === "Admin" ? ' <span class="role-pill admin">староста</span>' : ""}</div>
          ${progressBar(st.completed, st.total)}
        </div>
        <div class="stats-row-side">
          <span class="stats-row-count">${st.completed}/${st.total}</span>
          <span class="stats-row-sub ${st.overdue ? "bad" : ""}">${st.overdue ? `${st.overdue} просроч.` : "без долгов"}</span>
        </div>`;
      row.addEventListener("click", () => openStudentSheet(st.telegramId));
      rows.appendChild(row);
    });
  }
  statsList.innerHTML = "";
  statsList.appendChild(rows);
}

function personListHtml(people, emptyText, withTime) {
  if (people.length === 0) return `<div class="sheet-empty">${emptyText}</div>`;
  return `<div class="sheet-people">${people.map((p) => `
    <div class="sheet-person">
      <div class="user-avatar small">${escapeHtml((p.name || "?")[0].toUpperCase())}</div>
      <span class="sheet-person-name">${escapeHtml(p.name)}</span>
      ${withTime && p.completedAt ? `<span class="sheet-person-time">${new Date(p.completedAt).toLocaleDateString("ru-RU", { day: "numeric", month: "short" })}</span>` : ""}
    </div>`).join("")}</div>`;
}

async function openTaskProgressSheet(messageId) {
  openSheet("Загрузка…", "", skeleton("user", 3));
  try {
    const res = await apiFetch(`/admin/message/${messageId}/progress`);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const p = await res.json();
    const total = p.done.length + p.notDone.length;
    const overdue = p.deadline && new Date(p.deadline) < new Date();
    openSheet(plainPreview(p.text).slice(0, 80) || "Задание", `${p.groupName} · сдали ${p.done.length} из ${total}`, `
      ${progressBar(p.done.length, total)}
      <div class="sheet-section-title ${overdue && p.notDone.length ? "bad" : ""}">Не сдали · ${p.notDone.length}${overdue && p.notDone.length ? " · срок прошёл" : ""}</div>
      ${personListHtml(p.notDone, "Все сдали 🎉", false)}
      <div class="sheet-section-title">Сдали · ${p.done.length}</div>
      ${personListHtml(p.done, "Пока никто не сдал", true)}`);
  } catch (err) {
    console.error(err);
    openSheet("Ошибка", "", '<div class="sheet-empty">Не удалось загрузить данные.</div>');
  }
}

async function openStudentSheet(telegramId) {
  openSheet("Загрузка…", "", skeleton("deadline", 3));
  try {
    const res = await apiFetch(`/admin/stats/student/${telegramId}`);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const st = await res.json();
    const pendingHtml = st.pending.length === 0
      ? '<div class="sheet-empty">Все задания сданы 🎉</div>'
      : `<div class="deadline-section-items">${st.pending.map((t) => `
          <button type="button" class="stats-row" data-open-task="${t.id}" data-group="${t.groupId}">
            <div class="stats-row-body">
              <div class="deadline-item-subject">${getSubjectIcon(t.groupName)}<span>${escapeHtml(t.groupName)}</span></div>
              <div class="stats-row-title">${escapeHtml(plainPreview(t.text)) || "Без описания"}</div>
            </div>
            <div class="stats-row-side">
              <span class="stats-row-sub ${t.isOverdue ? "bad" : ""}">${t.deadline
                ? (t.isOverdue ? "просрочено" : new Date(t.deadline).toLocaleDateString("ru-RU", { day: "numeric", month: "short" }))
                : "без срока"}</span>
            </div>
          </button>`).join("")}</div>`;
    openSheet(st.name, `сдано ${st.completed} из ${st.total}`, `
      ${progressBar(st.completed, st.total)}
      <div class="sheet-section-title">Не сдано · ${st.pending.length}</div>
      ${pendingHtml}`);

    // A task in the list opens it in its subject
    sheetBody.querySelectorAll("[data-open-task]").forEach((btn) => {
      btn.addEventListener("click", () => {
        const groupId = Number(btn.dataset.group);
        const group = cachedGroups.find((g) => g.id === groupId);
        closeSheet();
        openChat(groupId, group?.name || "Предмет", group?.description,
          { returnTo: "usersScreen", highlightId: Number(btn.dataset.openTask) });
      });
    });
  } catch (err) {
    console.error(err);
    openSheet("Ошибка", "", '<div class="sheet-empty">Не удалось загрузить данные.</div>');
  }
}

// ============================================================
// SKELETON PLACEHOLDERS (instead of "Загрузка…")
// ============================================================
function skeleton(kind, count) {
  const one = {
    group: `<div class="group-item skeleton-item" aria-hidden="true">
              <div class="group-avatar skel"></div>
              <div class="group-info"><div class="skel skel-line w55"></div><div class="skel skel-line sm w80"></div></div>
            </div>`,
    message: `<div class="message-card skeleton-item" aria-hidden="true">
              <div class="skel skel-line sm w30"></div>
              <div class="skel skel-line w90"></div><div class="skel skel-line w65"></div>
              <div class="skel skel-pill"></div>
            </div>`,
    deadline: `<div class="deadline-item skeleton-item" aria-hidden="true">
              <div class="deadline-check skel"></div>
              <div class="deadline-item-body"><div class="skel skel-line sm w30"></div><div class="skel skel-line w80"></div></div>
              <div class="skel skel-line w30" style="width:44px"></div>
            </div>`,
    user: `<div class="user-card skeleton-item" aria-hidden="true">
              <div class="user-avatar skel"></div>
              <div class="user-info"><div class="skel skel-line w45"></div><div class="skel skel-line sm w70"></div></div>
            </div>`
  }[kind];
  return `<div class="skeleton-list" aria-label="Загрузка">${one.repeat(count)}</div>`;
}

// ============================================================
// SLIDING INDICATOR for segmented controls and the bottom nav
// ============================================================
const indicatorUpdaters = [];

/** Adds a pill that slides to the active item instead of the highlight jumping. */
function setupSlidingIndicator(container, itemSelector) {
  if (!container) return;
  const pill = document.createElement("span");
  pill.className = "slide-indicator";
  pill.setAttribute("aria-hidden", "true");
  container.prepend(pill);
  container.classList.add("has-indicator");

  const update = (animate = true) => {
    const active = container.querySelector(`${itemSelector}.active:not(.hidden)`);
    if (!active || active.offsetWidth === 0) {
      pill.style.opacity = "0";
      return;
    }
    if (!animate || pill.style.opacity === "0" || !pill.style.width) pill.classList.add("no-anim");
    pill.style.opacity = "1";
    pill.style.width = `${active.offsetWidth}px`;
    pill.style.height = `${active.offsetHeight}px`;
    pill.style.transform = `translate(${active.offsetLeft}px, ${active.offsetTop}px)`;
    pill.getBoundingClientRect();
    pill.classList.remove("no-anim");
  };

  // Follow "active" changes of the items (and items being shown/hidden by role)
  // (changes of the pill's own class are ignored, otherwise the observer would trigger itself forever)
  new MutationObserver((records) => {
    if (records.some((r) => r.target !== pill)) update();
  }).observe(container, { subtree: true, attributes: true, attributeFilter: ["class"] });
  indicatorUpdaters.push(update);
  requestAnimationFrame(() => update(false));
}

/** Re-measure all indicators (after a screen becomes visible, on resize, after fonts load). */
function refreshIndicators(animate = false) {
  indicatorUpdaters.forEach((update) => update(animate));
}

setupSlidingIndicator(document.querySelector(".groups-filter-tabs"), ".groups-filter-tab");
setupSlidingIndicator(document.querySelector(".chat-filter-tabs"), ".chat-filter-tab");
setupSlidingIndicator(document.querySelector(".deadline-filter-tabs"), ".deadline-filter-tab");
setupSlidingIndicator(document.querySelector(".users-tabs"), ".users-tab");
setupSlidingIndicator(document.querySelector(".stats-view-tabs"), ".stats-view-tab");
setupSlidingIndicator(document.querySelector(".theme-toggle-group"), ".theme-btn");
setupSlidingIndicator(document.querySelector(".bottom-nav"), ".nav-item");
window.addEventListener("resize", () => refreshIndicators(false));
document.fonts?.ready.then(() => refreshIndicators(false));

// ============================================================
// TELEGRAM BACK BUTTON
// ============================================================
const isInsideTelegram = () => Boolean(window.Telegram?.WebApp?.initData);

/** What the Back button should do right now (most specific first), or null. */
function currentBackAction() {
  if (!lightbox.classList.contains("hidden")) return closeLightbox;
  if (!sheet.classList.contains("hidden")) return closeSheet;
  if (!addUserModal.classList.contains("hidden")) return () => {
    addUserModal.classList.add("hidden");
    updateTelegramBackButton();
  };
  if (composer.editingId !== null) return () => {
    resetComposer();
    updateTelegramBackButton();
  };
  if (chatScreen.classList.contains("active")) return () => switchScreen(chatReturnScreen);
  return null;
}

function updateTelegramBackButton() {
  const action = currentBackAction();
  const inChat = chatScreen.classList.contains("active");
  const tgBack = window.Telegram?.WebApp?.BackButton;

  if (isInsideTelegram() && tgBack) {
    // Native button in the Telegram header replaces our own
    backBtn.classList.add("hidden");
    if (action) tgBack.show(); else tgBack.hide();
  } else {
    backBtn.classList.toggle("hidden", !inChat);
  }
}

window.Telegram?.WebApp?.BackButton?.onClick(() => {
  window.Telegram.WebApp.HapticFeedback?.impactOccurred("light");
  currentBackAction()?.();
});

// The add-user modal is opened/closed in several places — follow its visibility
new MutationObserver(updateTelegramBackButton).observe(addUserModal, { attributes: true, attributeFilter: ["class"] });

// ============================================================
// PULL TO REFRESH
// ============================================================
const PULL_THRESHOLD = 64;
const PULL_MAX = 96;

const pullIndicator = document.createElement("div");
pullIndicator.className = "pull-indicator";
pullIndicator.innerHTML = `<svg class="ico" viewBox="0 0 24 24"><path d="M21 12a9 9 0 1 1-3-6.7"/><path d="M21 3v6h-6"/></svg>`;
document.body.appendChild(pullIndicator);

let pull = { startY: null, distance: 0, refreshing: false };

function refreshCurrentScreen() {
  if (chatScreen.classList.contains("active") && currentGroupId) {
    return Promise.all([loadMessages(currentGroupId, true), loadDeadlinesSummary()]);
  }
  if (usersScreen.classList.contains("active")) return currentUsersTab === "stats" ? loadStats(true) : loadUsers();
  if (groupsScreen.classList.contains("active")) return loadGroups();
  if (deadlinesScreen.classList.contains("active")) return loadDeadlinesScreen(true);
  return null;
}

function setPullPosition(distance) {
  const progress = Math.min(distance / PULL_THRESHOLD, 1);
  pullIndicator.style.transform = `translate(-50%, ${distance - 48}px) rotate(${progress * 270}deg)`;
  pullIndicator.style.opacity = String(progress);
  pullIndicator.classList.toggle("ready", distance >= PULL_THRESHOLD);
}

document.addEventListener("touchstart", (e) => {
  if (pull.refreshing || window.scrollY > 0 || currentBackAction() === closeLightbox) return;
  // Do not hijack gestures inside inputs, the composer or horizontally scrolling chips
  if (e.target.closest("textarea, input, select, .chat-input-bar-wrapper, .filter-tabs, .modal-overlay, .sheet-overlay")) return;
  pull.startY = e.touches[0].clientY;
  pull.distance = 0;
}, { passive: true });

document.addEventListener("touchmove", (e) => {
  if (pull.startY === null) return;
  const dy = e.touches[0].clientY - pull.startY;
  if (dy <= 0 || window.scrollY > 0) {
    pull.startY = null;
    setPullPosition(0);
    return;
  }
  e.preventDefault(); // stop the page bounce while pulling
  const previous = pull.distance;
  pull.distance = Math.min(dy * 0.5, PULL_MAX);
  if (previous < PULL_THRESHOLD && pull.distance >= PULL_THRESHOLD) {
    window.Telegram?.WebApp?.HapticFeedback?.impactOccurred("light");
  }
  setPullPosition(pull.distance);
}, { passive: false });

document.addEventListener("touchend", async () => {
  if (pull.startY === null) return;
  const shouldRefresh = pull.distance >= PULL_THRESHOLD;
  pull.startY = null;

  if (!shouldRefresh) {
    setPullPosition(0);
    return;
  }

  const task = refreshCurrentScreen();
  if (!task) {
    setPullPosition(0);
    return;
  }

  pull.refreshing = true;
  pullIndicator.classList.add("spinning");
  pullIndicator.style.transform = `translate(-50%, ${PULL_THRESHOLD - 48}px)`;
  try {
    await task;
    window.Telegram?.WebApp?.HapticFeedback?.notificationOccurred("success");
  } finally {
    pull.refreshing = false;
    pullIndicator.classList.remove("spinning");
    setPullPosition(0);
  }
});

// ============================================================
// START
// ============================================================
initApp();
