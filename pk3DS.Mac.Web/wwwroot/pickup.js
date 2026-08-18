const $ = (id) => document.getElementById(id);
let game = null;
let items = [];
let original = null;

async function post(url, body = {}) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  const data = await response.json();
  if (!response.ok) throw new Error(data.error || data.detail || 'Operación fallida');
  return data;
}

function setStatus(message, state = 'neutral') {
  $('status').textContent = message;
  $('status').className = `status ${state}`;
}

function readEntries() {
  return [...document.querySelectorAll('.pickup-row')].map((row) => ({
    item: Number(row.querySelector('.pickup-item').value),
    rates: [...row.querySelectorAll('.pickup-rate')].map((input) => Number(input.value)),
  }));
}

function updateState() {
  const current = readEntries();
  const changed = JSON.stringify(current) !== JSON.stringify(original);
  $('summary').textContent = changed ? `${current.length} fila(s) modificada(s)` : 'Sin cambios para exportar';
  $('export').disabled = !game?.titleId || !changed || current.length === 0;
  $('add').disabled = !game;
  $('remove').disabled = !game || current.length <= 1;
}

function itemName(id) {
  return items.find((item) => item.id === id)?.name || `Objeto ${id}`;
}

function render(entries = original || []) {
  const container = $('entries');
  container.replaceChildren();

  const header = document.createElement('div');
  header.className = 'pickup-row pickup-head';
  const itemHeader = document.createElement('b');
  itemHeader.textContent = 'Objeto';
  header.append(itemHeader);
  for (let column = 0; column < 10; column++) {
    const label = document.createElement('b');
    label.textContent = `${column * 10 + 1}–${(column + 1) * 10}`;
    header.append(label);
  }
  container.append(header);

  entries.forEach((entry, rowIndex) => {
    const row = document.createElement('div');
    row.className = 'pickup-row';
    const item = document.createElement('select');
    item.className = 'pickup-item';
    items.forEach((candidate) => {
      const option = document.createElement('option');
      option.value = candidate.id;
      option.textContent = `${candidate.name} · ${String(candidate.id).padStart(3, '0')}`;
      option.selected = candidate.id === entry.item;
      item.append(option);
    });
    row.append(item);

    for (let column = 0; column < 10; column++) {
      const input = document.createElement('input');
      input.className = 'pickup-rate';
      input.type = 'number';
      input.min = '0';
      input.max = '100';
      input.value = entry.rates[column] ?? 0;
      input.setAttribute('aria-label', `${itemName(entry.item)}, niveles ${column * 10 + 1}-${(column + 1) * 10}`);
      row.append(input);
    }
    row.dataset.index = rowIndex;
    container.append(row);
  });
  updateState();
}

function emptyEntry() {
  return { item: items[0]?.id || 0, rates: Array(10).fill(10) };
}

async function loadWorkspace() {
  try {
    original = null;
    $('entries').replaceChildren();
    updateState();
    game = await post('/api/workspace/inspect', { workspacePath: $('workspace').value });
    if (!['SM', 'USUM'].includes(game.gameVersion)) {
      throw new Error('Pickup está disponible solo para Sol/Luna y Ultrasol/Ultraluna.');
    }
    setStatus(`${game.gameVersion} cargado. Pulsa Abrir tabla para leer Pickup.`, 'success');
    $('editor').classList.remove('is-disabled');
    $('add').disabled = false;
    await openTable();
  } catch (error) {
    game = null;
    original = null;
    $('editor').classList.add('is-disabled');
    $('entries').replaceChildren();
    setStatus(error.message, 'error');
    updateState();
  }
}

async function openTable() {
  try {
    const data = await post('/api/editors/pickup/table', { workspacePath: $('workspace').value, language: 2 });
    items = data.items;
    original = structuredClone(data.entries);
    render();
    setStatus(`${game.gameVersion} cargado: ${original.length} filas de Pickup.`, 'success');
  } catch (error) {
    setStatus(error.message, 'error');
  }
}

$('browse').onclick = async () => {
  try {
    $('workspace').value = (await post('/api/workspace/pick')).path;
    await loadWorkspace();
  } catch (error) {
    setStatus(error.message, 'error');
  }
};
$('load').onclick = loadWorkspace;
$('add').onclick = () => {
  const entries = readEntries();
  entries.push(emptyEntry());
  render(entries);
  updateState();
};
$('remove').onclick = () => {
  const entries = readEntries();
  if (entries.length <= 1) return;
  entries.pop();
  render(entries);
};
$('entries').oninput = updateState;
$('entries').onchange = updateState;
$('export').onclick = async () => {
  try {
    const output = await post('/api/workspace/pick-output');
    const result = await post('/api/editors/pickup/export', {
      workspacePath: $('workspace').value,
      outputDirectory: output.path,
      titleId: game.titleId,
      entries: readEntries(),
      language: 2,
    });
    original = readEntries();
    $('result').textContent = `Listo. ZIP: ${result.zipPath}`;
    $('result').className = 'status success';
    updateState();
  } catch (error) {
    $('result').textContent = error.message;
    $('result').className = 'status error';
    updateState();
  }
};
updateState();
