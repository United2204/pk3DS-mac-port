const $ = id => document.getElementById(id);
let game, table, original;

async function post(url, body = {}) {
  const response = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  const data = await response.json();
  if (!response.ok) throw Error(data.error || data.detail || 'Operación fallida');
  return data;
}

function changed() { return !!table && JSON.stringify(table) !== JSON.stringify(original); }
function ui() {
  $('summary').textContent = changed() ? 'Tablas modificadas' : 'Sin cambios para exportar';
  $('export').disabled = !game?.titleId || !changed();
}

function moveSelect(groupIndex, entryIndex, value) {
  const select = document.createElement('select');
  select.dataset.group = groupIndex;
  select.dataset.entry = entryIndex;
  select.dataset.field = 'move';
  for (const move of table.moves) {
    const option = new Option(`${move.id} · ${move.name}`, move.id);
    option.selected = move.id === value;
    select.add(option);
  }
  return select;
}

function priceInput(groupIndex, entryIndex, value) {
  const input = document.createElement('input');
  input.type = 'number';
  input.min = '0';
  input.max = '65535';
  input.value = value;
  input.dataset.group = groupIndex;
  input.dataset.entry = entryIndex;
  input.dataset.field = 'price';
  return input;
}

function render() {
  const container = $('groups');
  container.replaceChildren();
  table.groups.forEach((group, groupIndex) => {
    const section = document.createElement('section');
    const heading = document.createElement('h2');
    heading.textContent = group.name;
    const list = document.createElement('div');
    list.className = 'tutor-list';
    group.entries.forEach((entry, entryIndex) => {
      const row = document.createElement('div');
      row.className = 'tutor-row';
      const label = document.createElement('b');
      label.textContent = `Tutor ${String(entryIndex + 1).padStart(2, '0')}`;
      const move = moveSelect(groupIndex, entryIndex, entry.move);
      const price = priceInput(groupIndex, entryIndex, entry.price);
      row.append(label, move, price);
      list.append(row);
    });
    section.append(heading, list);
    container.append(section);
  });
  $('warning').textContent = table.warning;
  ui();
}

async function load() {
  try {
    game = await post('/api/workspace/inspect', { workspacePath: $('workspace').value });
    $('open').disabled = false;
    $('editor').classList.remove('is-disabled');
    $('status').textContent = `${game.gameVersion} cargado.`;
  } catch (error) { $('status').textContent = error.message; }
  ui();
}

async function open() {
  try {
    table = await post('/api/editors/tutors/table', { workspacePath: $('workspace').value, language: 2 });
    original = structuredClone(table);
    render();
  } catch (error) { $('result').textContent = error.message; }
}

$('browse').onclick = async () => {
  try { $('workspace').value = (await post('/api/workspace/pick')).path; await load(); }
  catch (error) { $('status').textContent = error.message; }
};
$('load').onclick = load;
$('open').onclick = open;
$('groups').oninput = event => {
  const { group, entry, field } = event.target.dataset;
  if (group === undefined || entry === undefined) return;
  table.groups[+group].entries[+entry][field] = +event.target.value;
  ui();
};
$('export').onclick = async () => {
  try {
    const output = await post('/api/workspace/pick-output');
    const result = await post('/api/editors/tutors/export', { workspacePath: $('workspace').value, outputDirectory: output.path, titleId: game.titleId, groups: table.groups, language: 2 });
    original = structuredClone(table);
    $('result').textContent = `Listo. ZIP: ${result.zipPath}`;
    ui();
  } catch (error) { $('result').textContent = error.message; }
};
ui();
