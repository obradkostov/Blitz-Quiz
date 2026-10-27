const $ = id => document.getElementById(id);

let gameId, player, qId, timerId;
let total = 0;
let answered = false;

// Pomoćna funkcija za pozive ka serveru (C# endpointi)
async function api(url, method = "GET", body) {
  const r = await fetch(url, {
    method,
    headers: { "Content-Type": "application/json" },
    body: body ? JSON.stringify(body) : undefined
  });
  return { ok: r.ok, data: await r.json().catch(() => ({})) };
}

// ---------- Početak partije ----------
$("startBtn").onclick = async () => {
  player = $("playerName").value.trim();
  if (!player) return;

  const r = await api("/api/game", "POST");
  gameId = r.data.gameId;
  total = 0;

  $("final").textContent = "";
  $("start").classList.add("hidden");
  $("game").classList.remove("hidden");
  nextQuestion();
};

$("nextBtn").onclick = nextQuestion;

// ---------- Pitanje ----------
async function nextQuestion() {
  clearInterval(timerId);
  $("nextBtn").classList.add("hidden");
  $("result").textContent = "";
  answered = false;

  const r = await api(`/api/game/${gameId}/next`);
  if (r.data.finished) return finish();
  if (!r.ok) {
    $("result").textContent = r.data.error || "Greška";
    return;
  }

  const q = r.data;
  qId = q.questionId;
  $("meta").textContent =
    `Runda ${q.round}/10 · ${q.category} · težina ${q.difficulty} · poeni: ${total}`;
  $("qtext").textContent = q.text;

  $("options").innerHTML = "";
  q.options.forEach((text, i) => {
    const b = document.createElement("button");
    b.textContent = text;
    b.onclick = () => answer(i);
    $("options").appendChild(b);
  });

  startTimer(q.seconds);
}

function startTimer(seconds) {
  let left = seconds;
  $("timer").textContent = left;
  timerId = setInterval(() => {
    left--;
    $("timer").textContent = left;
    if (left <= 0) {
      clearInterval(timerId);
      if (!answered) timeUp();
    }
  }, 1000);
}

function lockButtons() {
  [...$("options").children].forEach(b => (b.disabled = true));
}

function timeUp() {
  answered = true;
  lockButtons();
  $("result").textContent = "Vreme je isteklo.";
  $("nextBtn").classList.remove("hidden");
}

// ---------- Odgovor ----------
async function answer(i) {
  if (answered) return;
  answered = true;
  clearInterval(timerId);

  const r = await api(`/api/game/${gameId}/answer`, "POST",
    { player, questionId: qId, answer: i });

  const buttons = $("options").children;
  if (!r.ok) {
    $("result").textContent = r.data.error || "Greška";
  } else {
    total = r.data.total;
    buttons[i].classList.add(r.data.correct ? "ok" : "bad");
    buttons[r.data.correctAnswer].classList.add("ok");
    $("result").textContent = r.data.correct
      ? `Tačno! +${r.data.points}`
      : "Netačno.";
  }

  lockButtons();
  $("nextBtn").classList.remove("hidden");
  loadBoard();
}

// ---------- Kraj partije ----------
function finish() {
  $("game").classList.add("hidden");
  $("start").classList.remove("hidden");
  $("final").textContent = `Kraj partije! Ukupno poena: ${total}`;
  loadBoard();
}

// ---------- Rang lista ----------
async function loadBoard() {
  const r = await api("/api/leaderboard");
  const board = $("board");
  board.innerHTML = "";

  (r.data || []).forEach((p, i) => {
    const tr = document.createElement("tr");
    const name = document.createElement("td");
    const score = document.createElement("td");
    name.textContent = `${i + 1}. ${p.player}`;
    score.textContent = p.score;
    tr.append(name, score);
    board.appendChild(tr);
  });
}

loadBoard();
setInterval(loadBoard, 2000);