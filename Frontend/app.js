// Configuration: API endpoint
const API_BASE = window.location.protocol.startsWith("http")
  ? `${window.location.origin}/api`
  : "http://localhost:5000/api";

// State
let currentRole = "User"; // 'User' | 'Admin' | 'Developer'
let currentGroupId = null;
let currentGroupName = "";
let telegramUser = null;

// DOM Elements
const groupsScreen = document.getElementById("groupsScreen");
const chatScreen = document.getElementById("chatScreen");
const groupsList = document.getElementById("groupsList");
const groupsCount = document.getElementById("groupsCount");
const messagesContainer = document.getElementById("messagesContainer");
const backBtn = document.getElementById("backBtn");
const headerTitle = document.getElementById("headerTitle");
const roleSelector = document.getElementById("roleSelector");
const devPanel = document.getElementById("devPanel");
const adminPostArea = document.getElementById("adminPostArea");
const currentChatName = document.getElementById("currentChatName");
const currentChatDesc = document.getElementById("currentChatDesc");
const adminMessageInput = document.getElementById("adminMessageInput");
const sendMessageBtn = document.getElementById("sendMessageBtn");
const createGroupBtn = document.getElementById("createGroupBtn");
const newGroupName = document.getElementById("newGroupName");
const newGroupDesc = document.getElementById("newGroupDesc");

// Initialize Telegram WebApp SDK if available
if (window.Telegram && window.Telegram.WebApp) {
  const tg = window.Telegram.WebApp;
  tg.ready();
  tg.expand();
  if (tg.initDataUnsafe && tg.initDataUnsafe.user) {
    telegramUser = tg.initDataUnsafe.user;
    console.log("Telegram user:", telegramUser);
  }
}

// Role change handler
roleSelector.addEventListener("change", (e) => {
  currentRole = e.target.value;
  updateRoleVisibility();
});

function updateRoleVisibility() {
  if (currentRole === "Developer") {
    devPanel.classList.remove("hidden");
  } else {
    devPanel.classList.add("hidden");
  }

  if (currentRole === "Admin" && currentGroupId !== null) {
    adminPostArea.classList.remove("hidden");
  } else {
    adminPostArea.classList.add("hidden");
  }

  // Re-render group list to show/hide delete buttons
  renderGroupsList(cachedGroups);
}

let cachedGroups = [];

// Fetch groups from backend
async function loadGroups() {
  groupsList.innerHTML = '<div class="loader">Загрузка предметов...</div>';
  try {
    const res = await fetch(`${API_BASE}/admin/groups`);
    if (!res.ok) throw new Error("Не удалось загрузить группы");
    cachedGroups = await res.json();
    renderGroupsList(cachedGroups);
  } catch (err) {
    console.error("Ошибка соединения с API:", err);
    groupsList.innerHTML = `
      <div class="empty-state">
        <p>⚠️ Не удалось связаться с сервером API.</p>
        <p style="font-size:0.8rem; color:#64748b;">Убедитесь, что backend запущен на <b>${API_BASE}</b></p>
        <button class="btn primary-btn" onclick="loadGroups()" style="margin-top:10px;">Повторить</button>
      </div>`;
  }
}

// Render groups
function renderGroupsList(groups) {
  if (!groups || groups.length === 0) {
    groupsList.innerHTML = '<div class="empty-state">Пока нет созданных предметов</div>';
    groupsCount.textContent = "0 групп";
    return;
  }

  groupsCount.textContent = `${groups.length} групп`;
  groupsList.innerHTML = "";

  groups.forEach((g) => {
    const item = document.createElement("div");
    item.className = "group-item";

    const isDev = currentRole === "Developer";
    item.innerHTML = `
      <div class="group-info" style="flex:1;">
        <h3>📁 ${escapeHtml(g.name)}</h3>
        <p>${escapeHtml(g.description || "Без описания")}</p>
      </div>
      ${isDev ? `<button class="delete-btn" title="Удалить предмет" data-id="${g.id}">🗑️</button>` : ""}
    `;

    item.querySelector(".group-info").addEventListener("click", () => {
      openChat(g.id, g.name, g.description);
    });

    if (isDev) {
      const delBtn = item.querySelector(".delete-btn");
      if (delBtn) {
        delBtn.addEventListener("click", (e) => {
          e.stopPropagation();
          deleteGroup(g.id, g.name);
        });
      }
    }

    groupsList.appendChild(item);
  });
}

// Open Chat
async function openChat(groupId, name, desc) {
  currentGroupId = groupId;
  currentGroupName = name;

  headerTitle.textContent = name;
  currentChatName.textContent = name;
  currentChatDesc.textContent = desc || "Все актуальные задания и лекции";

  backBtn.classList.remove("hidden");
  groupsScreen.classList.remove("active");
  chatScreen.classList.add("active");

  updateRoleVisibility();
  await loadMessages(groupId);
}

// Back to Groups list
backBtn.addEventListener("click", () => {
  currentGroupId = null;
  backBtn.classList.add("hidden");
  headerTitle.textContent = "Предметы и ДЗ";
  chatScreen.classList.remove("active");
  groupsScreen.classList.add("active");
  adminPostArea.classList.add("hidden");
});

// Load messages for a group
async function loadMessages(groupId) {
  messagesContainer.innerHTML = '<div class="loader">Загрузка сообщений...</div>';
  try {
    const res = await fetch(`${API_BASE}/admin/group/${groupId}/messages`);
    const msgs = await res.json();
    renderMessages(msgs);
  } catch (err) {
    console.error(err);
    messagesContainer.innerHTML = '<div class="empty-state">Ошибка загрузки сообщений</div>';
  }
}

// Render messages
function renderMessages(messages) {
  if (!messages || messages.length === 0) {
    messagesContainer.innerHTML = '<div class="empty-state">Нет записей. Администратор еще не публиковал ДЗ.</div>';
    return;
  }

  messagesContainer.innerHTML = "";
  messages.forEach((m) => {
    const card = document.createElement("div");
    card.className = "message-card";

    const date = new Date(m.sentAt);
    const dateFormatted = date.toLocaleDateString("ru-RU", { day: "2-digit", month: "long" });
    const timeFormatted = date.toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" });

    card.innerHTML = `
      <div class="message-meta">
        <span>👤 ${escapeHtml(m.author || "Администратор")}</span>
        <span>📅 ${dateFormatted}, ${timeFormatted}</span>
      </div>
      <div class="message-text">${escapeHtml(m.text)}</div>
    `;
    messagesContainer.appendChild(card);
  });

  // Scroll to bottom
  messagesContainer.scrollTop = messagesContainer.scrollHeight;
}

// Admin: Post message
sendMessageBtn.addEventListener("click", async () => {
  const text = adminMessageInput.value.trim();
  if (!text || !currentGroupId) return;

  sendMessageBtn.disabled = true;
  sendMessageBtn.textContent = "Отправка...";

  try {
    const res = await fetch(`${API_BASE}/admin/group/${currentGroupId}/message`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        text: text,
        fromUserId: telegramUser ? telegramUser.id : 0
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
    sendMessageBtn.textContent = "Отправить";
  }
});

// Developer: Create new group
createGroupBtn.addEventListener("click", async () => {
  const name = newGroupName.value.trim();
  const desc = newGroupDesc.value.trim();
  if (!name) {
    alert("Введите название группы/предмета!");
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
      await loadGroups();
    } else {
      alert("Ошибка при создании группы");
    }
  } catch (e) {
    console.error(e);
    alert("Ошибка соединения");
  } finally {
    createGroupBtn.disabled = false;
  }
});

// Developer: Delete group
async function deleteGroup(id, name) {
  if (!confirm(`Вы действительно хотите удалить предмет «${name}» и все его сообщения?`)) {
    return;
  }

  try {
    const res = await fetch(`${API_BASE}/admin/group/${id}`, { method: "DELETE" });
    if (res.ok) {
      await loadGroups();
    } else {
      alert("Не удалось удалить группу");
    }
  } catch (e) {
    console.error(e);
    alert("Ошибка соединения");
  }
}

function escapeHtml(str) {
  if (!str) return "";
  return String(str)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#039;");
}

// Initial load
loadGroups();
