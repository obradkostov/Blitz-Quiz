const $ = id => document.getElementById(id);
let editingId = null;   // null = novo pitanje, broj = menjamo postojeće

async function api(url, method = "GET", body) {
  const r = await fetch(url, {
    method,
    headers: { "Content-Type": "application/json" },
    body: body ? JSON.stringify(body) : undefined
  });
  return { ok: r.ok, data: await r.json().catch(() => ({})) };
}

// READ: spisak pitanja izabrane težine
async function loadList() {
  const r = await api(`/api/questions?difficulty=${$("filter").value}`);
  $("list").innerHTML = "";
  for (const q of r.data) {
    const b = document.createElement("button");
    b.textContent = `#${q.id} ${q.text}`;
    b.onclick = () => openQuestion(q.id);
    $("list").appendChild(b);
  }
}

// READ: jedno pitanje ide u formu
async function openQuestion(id) {
  const q = (await api(`/api/questions/${id}`)).data;
  editingId = id;
  $("formTitle").textContent = `Izmena pitanja #${id}`;
  $("text").value = q.text;
  document.querySelectorAll(".opt").forEach((el, i) => (el.value = q.options[i]));
  $("correct").value = q.correct;
  $("category").value = q.category;
  $("difficulty").value = q.difficulty;
  $("del").classList.remove("hidden");
  $("msg").textContent = "";
}

function clearForm() {
  editingId = null;
  $("formTitle").textContent = "Novo pitanje";
  document.querySelectorAll("#text, .opt, #category").forEach(el => (el.value = ""));
  $("del").classList.add("hidden");
}

// CREATE (nema id) ili UPDATE (ima id)
async function save() {
  const body = {
    text: $("text").value,
    options: [...document.querySelectorAll(".opt")].map(el => el.value),
    correct: Number($("correct").value),
    category: $("category").value,
    difficulty: Number($("difficulty").value)
  };
  const r = editingId
    ? await api(`/api/questions/${editingId}`, "PUT", body)
    : await api("/api/questions", "POST", body);
  $("msg").textContent = r.ok ? "Sačuvano." : r.data.error;
  if (r.ok) { clearForm(); loadList(); }
}

// DELETE
async function remove() {
  if (!confirm("Obrisati pitanje?")) return;
  const r = await api(`/api/questions/${editingId}`, "DELETE");
  $("msg").textContent = r.ok ? "Obrisano." : r.data.error;
  if (r.ok) { clearForm(); loadList(); }
}

$("save").onclick = save;
$("del").onclick = remove;
$("clear").onclick = clearForm;
$("filter").onchange = loadList;
loadList();