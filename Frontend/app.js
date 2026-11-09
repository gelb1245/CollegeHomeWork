// API Base URL
const API_BASE = window.location.protocol.startsWith("http")
  ? `${window.location.origin}/api`
  : "http://localhost:5000/api";

const PRIMARY_OWNER_ID = 1645991235;

// Global App State
let currentUser = {
  id: PRIMARY_OWNER_ID,
  username: "Alexandr",
  role: "Developer"
};

let currentScreen = "groupsScreen";
let currentGroupId = null;
let currentGroupName = "";

let cachedGroups = [];
let cachedUsers = [];
let currentRoleFilter = "all";

// DOM Elements
const backBtn = document.getElementById("backBtn");
const headerTitle = document.getElementById("headerTitle");
const userStatusSub = document.getElementById("userStatusSub");
const roleSelector = document.getElementById("roleSelector");

const navItems = document.querySelectorAll(".nav-item");
const navUsersTab = document.getElementById("navUsersTab");
const navSettingsTab = document.getElementById("navSettingsTab");

const groupsScreen = document.getElementById("groupsScreen");
const chatScreen = document.getElementById("chatScreen");
const usersScreen = document.getElementById("usersScreen");
const settingsScreen = document.getElementById("settingsScreen");

const groupsList = document.getElementById("groupsList");
const groupsCounter = document.getElementById("groupsCounter");
const groupSearchInput = document.getElementById("groupSearchInput");

const currentChatName = document.getElementById("currentChatName");
const currentChatDesc = document.getElementById("currentChatDesc");
const chatAdminActions = document.getElementById("chatAdminActions");
const deleteCurrentGroupBtn = document.getElementById("deleteCurrentGroupBtn");
const messagesContainer = document.getElementById("messagesContainer");
const adminPostArea = document.getElementById("adminPostArea");
const adminMessageInput = document.getElementById("adminMessageInput");
const sendMessageBtn = document.getElementById("sendMessageBtn");

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

// --- Initialization ---
async function initApp() {
  // 1. Check if inside Telegram WebApp
  if (window.Telegram && window.Telegram.WebApp) {
    const tg = window.Telegram.WebApp;
    tg.ready();
    tg.expand();

    if (tg.initDataUnsafe && tg.initDataUnsafe.user) {
      const u = tg.initDataUnsafe.user;
      currentUser.id = u.id;
      currentUser.username = u.username || u.first_name || "Пользователь";

      // Fetch user profile & role from backend
      try {
        const res = await fetch(`${API_BASE}/admin/me/${u.id}`);
        if (res.ok) {
          const data = await res.json();
          currentUser.role = formatRoleName(data.role);
        }
      } catch (err) {
        console.warn("Could not fetch remote profile:", err);
      }
    }
  }

  // Update UI with current role
  syncRoleUI();

  // Load initial groups
  await loadGroups();
}

function formatRoleName(roleNumOrStr) {
  if (roleNumOrStr === 0 || roleNumOrStr === "Developer") return "Developer";
  if (roleNumOrStr === 1 || roleNumOrStr === "Admin") return "Admin";
  return "User";
}

// Sync Role UI (Permissions)
function syncRoleUI() {
  roleSelector.value = currentUser.role;

  let roleText = "🎓 Студент";
  if (currentUser.role === "Developer") roleText = "👨‍💻 Разработчик (полный доступ)";
  else if (currentUser.role === "Admin") roleText = "👑 Администратор";

  userStatusSub.textContent = `${currentUser.username} • ${roleText}`;

  // Hide or show bottom navigation tabs based on role
  if (currentUser.role === "User") {
    navUsersTab.classList.add("hidden");
    navSettingsTab.classList.add("hidden");
  } else if (currentUser.role === "Admin") {
    navUsersTab.classList.remove("hidden");
    navSettingsTab.classList.add("hidden");
  } else {
    // Developer
    navUsersTab.classList.remove("hidden");
    navSettingsTab.classList.remove("hidden");
  }

  // Chat input
  if ((currentUser.role === "Admin" || currentUser.role === "Developer") && currentGroupId) {
    adminPostArea.classList.remove("hidden");
  } else {
    adminPostArea.classList.add("hidden");
  }

  // Chat delete subject button
  if (currentUser.role === "Developer" && currentGroupId) {
    chatAdminActions.classList.remove("hidden");
  } else {
    chatAdminActions.classList.add("hidden");
  }
}

// Role selector event (for testing)
roleSelector.addEventListener("change", (e) => {
  currentUser.role = e.target.value;
  syncRoleUI();
  if (currentScreen === "usersScreen") renderUsersList(cachedUsers);
  if (currentScreen === "chatScreen" && currentGroupId) loadMessages(currentGroupId);
});

// Navigation Bar
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
  document.getElementById(screenId).classList.add("active");

  navItems.forEach((btn) => {
    btn.classList.toggle("active", btn.dataset.screen === screenId);
  });

  backBtn.classList.add("hidden");

  if (screenId === "groupsScreen") {
    headerTitle.textContent = "Предметы и ДЗ";
    loadGroups();
  } else if (screenId === "usersScreen") {
    headerTitle.textContent = "Участники";
    loadUsers();
  } else if (screenId === "settingsScreen") {
    headerTitle.textContent = "Настройки";
  }

  syncRoleUI();
}

// Back Button
backBtn.addEventListener("click", () => {
  switchScreen("groupsScreen");
});

// ==========================================
// 1. GROUPS / SUBJECTS
// ==========================================
async function loadGroups() {
  groupsList.innerHTML = '<div class="loader">Загрузка предметов...</div>';
  try {
    const res = await fetch(`${API_BASE}/admin/groups`);
    if (!res.ok) throw new Error("Не удалось загрузить группы");
    cachedGroups = await res.json();
    renderGroupsList(cachedGroups);
  } catch (err) {
    console.error(err);
    groupsList.innerHTML = `<div class="empty-state">⚠️ Ошибка связи с API (${API_BASE}). Запустите backend.</div>`;
  }
}

function renderGroupsList(groups) {
  const query = groupSearchInput.value.toLowerCase().trim();
  const filtered = groups.filter((g) =>
    g.name.toLowerCase().includes(query) || (g.description && g.description.toLowerCase().includes(query))
  );

  groupsCounter.textContent = filtered.length;

  if (filtered.length === 0) {
    groupsList.innerHTML = '<div class="empty-state">Предметы не найдены.</div>';
    return;
  }

  groupsList.innerHTML = "";
  filtered.forEach((g) => {
    const item = document.createElement("div");
    item.className = "group-item";
    item.innerHTML = `
      <div class="group-avatar">📁</div>
      <div class="group-info">
        <h3>${escapeHtml(g.name)}</h3>
        <p>${escapeHtml(g.description || "Задания и дедлайны")}</p>
      </div>
      <span class="arrow-icon">›</span>
    `;
    item.addEventListener("click", () => openChat(g.id, g.name, g.description));
    groupsList.appendChild(item);
  });
}

groupSearchInput.addEventListener("input", () => renderGroupsList(cachedGroups));

// Open Chat for Subject
async function openChat(groupId, name, desc) {
  currentGroupId = groupId;
  currentGroupName = name;

  headerTitle.textContent = name;
  currentChatName.textContent = name;
  currentChatDesc.textContent = desc || "Все актуальные задания и дедлайны";

  document.querySelectorAll(".screen").forEach((s) => s.classList.remove("active"));
  chatScreen.classList.add("active");
  backBtn.classList.remove("hidden");

  syncRoleUI();
  await loadMessages(groupId);
}

// Load Messages
async function loadMessages(groupId) {
  messagesContainer.innerHTML = '<div class="loader">Загрузка заданий...</div>';
  try {
    const res = await fetch(`${API_BASE}/admin/group/${groupId}/messages`);
    const msgs = await res.json();
    renderMessages(msgs);
  } catch (err) {
    console.error(err);
    messagesContainer.innerHTML = '<div class="empty-state">Ошибка загрузки заданий</div>';
  }
}

function renderMessages(messages) {
  if (!messages || messages.length === 0) {
    messagesContainer.innerHTML = '<div class="empty-state">Пока нет записей. Администратор еще не опубликовал ДЗ.</div>';
    return;
  }

  messagesContainer.innerHTML = "";
  messages.forEach((m) => {
    const card = document.createElement("div");
    card.className = "message-card";

    const date = new Date(m.sentAt);
    const dateFormatted = date.toLocaleDateString("ru-RU", { day: "2-digit", month: "short" });
    const timeFormatted = date.toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" });

    const isDev = currentUser.role === "Developer";

    card.innerHTML = `
      <div class="message-card-header">
        <span class="message-author">✍️ ${escapeHtml(m.author)}</span>
        <div>
          <span class="message-date">${dateFormatted}, ${timeFormatted}</span>
          ${isDev ? `<button class="delete-msg-btn" data-msg-id="${m.id}" title="Удалить запись">✕</button>` : ""}
        </div>
      </div>
      <div class="message-text">${escapeHtml(m.text)}</div>
    `;

    if (isDev) {
      const delBtn = card.querySelector(".delete-msg-btn");
      if (delBtn) {
        delBtn.addEventListener("click", () => deleteMessage(m.id));
      }
    }

    messagesContainer.appendChild(card);
  });

  messagesContainer.scrollTop = messagesContainer.scrollHeight;
}

// Send Message (Admin / Developer)
sendMessageBtn.addEventListener("click", async () => {
  const text = adminMessageInput.value.trim();
  if (!text || !currentGroupId) return;

  sendMessageBtn.disabled = true;
  try {
    const res = await fetch(`${API_BASE}/admin/group/${currentGroupId}/message`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        text: text,
        fromUserId: currentUser.id
      })
    });

    if (res.ok) {
      adminMessageInput.value = "";
      await loadMessages(currentGroupId);
    } else {
      alert("Не удалось отправить сообщение");
    }
  } catch (e) {
    console.error(e);
    alert("Ошибка соединения");
  } finally {
    sendMessageBtn.disabled = false;
  }
});

// Delete Message (Developer)
async function deleteMessage(id) {
  if (!confirm("Удалить это задание?")) return;
  try {
    const res = await fetch(`${API_BASE}/admin/message/${id}`, { method: "DELETE" });
    if (res.ok) {
      await loadMessages(currentGroupId);
    }
  } catch (e) {
    console.error(e);
  }
}

// Delete Entire Subject (Developer)
deleteCurrentGroupBtn.addEventListener("click", async () => {
  if (!confirm(`Вы действительно хотите удалить предмет «${currentGroupName}» и все его задания?`)) return;

  try {
    const res = await fetch(`${API_BASE}/admin/group/${currentGroupId}`, { method: "DELETE" });
    if (res.ok) {
      switchScreen("groupsScreen");
    } else {
      alert("Не удалось удалить предмет");
    }
  } catch (e) {
    console.error(e);
    alert("Ошибка соединения");
  }
});

// ==========================================
// 2. USERS & WHITELIST MANAGEMENT
// ==========================================
async function loadUsers() {
  usersList.innerHTML = '<div class="loader">Загрузка участников...</div>';
  try {
    const res = await fetch(`${API_BASE}/admin/users`);
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
    if (roleName === "Developer") {
      roleClass = "dev";
      roleLabel = "Разработчик";
    } else if (roleName === "Admin") {
      roleClass = "admin";
      roleLabel = "Администратор";
    }

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
        ${
          isDev && !isSelf
            ? `
          <select class="user-role-select" data-user-id="${u.telegramId}">
            <option value="User" ${roleName === "User" ? "selected" : ""}>Студент</option>
            <option value="Admin" ${roleName === "Admin" ? "selected" : ""}>Админ</option>
            <option value="Developer" ${roleName === "Developer" ? "selected" : ""}>Разработчик</option>
          </select>
          <button class="delete-user-btn" data-del-user="${u.telegramId}" title="Удалить из системы">🗑️</button>
        `
            : ""
        }
      </div>
    `;

    // Role change dropdown event
    const select = card.querySelector(".user-role-select");
    if (select) {
      select.addEventListener("change", async (e) => {
        const newRole = e.target.value;
        await changeUserRole(u.telegramId, newRole);
      });
    }

    // Delete user button event
    const delBtn = card.querySelector(".delete-user-btn");
    if (delBtn) {
      delBtn.addEventListener("click", async () => {
        await deleteUser(u.telegramId, u.fullName || u.username);
      });
    }

    usersList.appendChild(card);
  });
}

// Change User Role (Developer)
async function changeUserRole(telegramId, role) {
  try {
    const res = await fetch(`${API_BASE}/admin/user/${telegramId}/role/${role}`, {
      method: "PATCH"
    });
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

// Delete User (Developer)
async function deleteUser(telegramId, name) {
  if (!confirm(`Удалить участника «${name}» (ID: ${telegramId}) из вайтлиста и запретить доступ к боту?`)) {
    return;
  }

  try {
    const res = await fetch(`${API_BASE}/admin/user/${telegramId}`, {
      method: "DELETE"
    });
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

// Filter Tabs
filterTabs.forEach((tab) => {
  tab.addEventListener("click", () => {
    filterTabs.forEach((t) => t.classList.remove("active"));
    tab.classList.add("active");
    currentRoleFilter = tab.dataset.role;
    renderUsersList(cachedUsers);
  });
});

userSearchInput.addEventListener("input", () => renderUsersList(cachedUsers));

// Modal: Add User
addUserModalBtn.addEventListener("click", () => {
  addUserModal.classList.remove("hidden");
});

closeModalBtn.addEventListener("click", () => {
  addUserModal.classList.add("hidden");
});

saveUserBtn.addEventListener("click", async () => {
  const id = parseInt(manualUserId.value);
  const name = manualUserName.value.trim();
  const role = manualUserRole.value;

  if (!id || id <= 0) {
    alert("Введите корректный числовой Telegram ID!");
    return;
  }

  try {
    const res = await fetch(`${API_BASE}/admin/user`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
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
      alert("Ошибка при сохранении пользователя");
    }
  } catch (e) {
    console.error(e);
    alert("Ошибка соединения");
  }
});

// ==========================================
// 3. SETTINGS & CREATE GROUP (Developer)
// ==========================================
createGroupBtn.addEventListener("click", async () => {
  const name = newGroupName.value.trim();
  const desc = newGroupDesc.value.trim();
  if (!name) {
    alert("Введите название предмета!");
    return;
  }

  createGroupBtn.disabled = true;
  try {
    const res = await fetch(`${API_BASE}/admin/group`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name, description: desc })
    });

    if (res.ok) {
      newGroupName.value = "";
      newGroupDesc.value = "";
      alert(`Предмет «${name}» успешно создан!`);
      switchScreen("groupsScreen");
    } else {
      alert("Не удалось создать предмет");
    }
  } catch (e) {
    console.error(e);
    alert("Ошибка связи");
  } finally {
    createGroupBtn.disabled = false;
  }
});

function escapeHtml(str) {
  if (!str) return "";
  return String(str)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#039;");
}

// Start
initApp();
