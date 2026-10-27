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
async function startGame() {
    player = $("playerName").value.trim();
    if (!player) {
        $("nameError").textContent = "Upiši ime da bi počeo partiju.";
        $("playerName").classList.add("invalid");
        $("playerName").focus();
        return;
    }

    const r = await api("/api/game", "POST");
    gameId = r.data.gameId;
    total = 0;

    $("final").textContent = "";
    $("start").classList.add("hidden");
    $("game").classList.remove("hidden");
    nextQuestion();
}

$("startBtn").onclick = startGame;
$("nextBtn").onclick = nextQuestion;

$("playerName").addEventListener("keydown", e => {
    if (e.key === "Enter") startGame();
});

// Čim počne da kuca, poruka o grešci nestaje
$("playerName").addEventListener("input", () => {
    $("nameError").textContent = "";
    $("playerName").classList.remove("invalid");
});

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
    const fill = $("barFill");

    // Traka se vraća na 100% bez animacije, pa se animira do 0
    fill.style.transition = "none";
    fill.style.width = "100%";
    void fill.offsetWidth; // prisilno osvežavanje, da browser primeni 100%
    fill.style.transition = "";

    $("timer").textContent = left;
    $("timer").classList.remove("low");

    timerId = setInterval(() => {
        left--;
        $("timer").textContent = left;
        fill.style.width = `${(left / seconds) * 100}%`;
        if (left <= 5) $("timer").classList.add("low");
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

    const rows = Array.isArray(r.data) ? r.data : [];
    if (rows.length === 0) {
        const tr = document.createElement("tr");
        tr.className = "empty";
        const td = document.createElement("td");
        td.colSpan = 2;
        td.textContent = "Još nema rezultata. Budi prvi!";
        tr.appendChild(td);
        board.appendChild(tr);
        return;
    }

    const medals = ["🥇", "🥈", "🥉"];
    rows.forEach((p, i) => {
        const tr = document.createElement("tr");
        if (p.player === player) tr.className = "me";
        const name = document.createElement("td");
        const score = document.createElement("td");
        name.textContent = `${medals[i] ?? i + 1 + "."} ${p.player}`;
        score.textContent = p.score;
        tr.append(name, score);
        board.appendChild(tr);
    });
}

loadBoard();
// ---------- Uživo: poruke preko SignalR-a ----------
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hub/feed")
    .withAutomaticReconnect()
    .build();

connection.on("gameFinished", json => {
    const e = JSON.parse(json);
    const li = document.createElement("li");
    li.textContent = `${e.player} je završio partiju: ${e.score} poena`;
    $("feed").prepend(li);
    while ($("feed").children.length > 8) $("feed").lastChild.remove();
    loadBoard(); // Top 10 se osvežava uživo
});

connection.start().catch(console.error);