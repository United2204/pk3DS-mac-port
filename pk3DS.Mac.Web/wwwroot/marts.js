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
  $('summary').textContent = changed() ? 'Inventarios modificados' : 'Sin cambios para exportar';
  $('export').disabled = !game?.titleId || !changed();
}
function selectItem(groupType, groupIndex, entryIndex, value) {
  const select = document.createElement('select');
  select.dataset.groupType = groupType;
  select.dataset.group = groupIndex;
  select.dataset.entry = entryIndex;
  select.dataset.field = 'item';
  for (const item of table.items) {
    const option = new Option(`${item.id} · ${item.name}`, item.id);
    option.selected = item.id === value;
    select.add(option);
  }
  return select;
}
function priceInput(groupType, groupIndex, entryIndex, value) {
  if (value === null || value === undefined) return document.createElement('span');
  const input = document.createElement('input');
  input.type = 'number'; input.min = '0'; input.max = '65535'; input.value = value;
  input.dataset.groupType = groupType; input.dataset.group = groupIndex; input.dataset.entry = entryIndex; input.dataset.field = 'price';
  return input;
}
function renderGroups(id, groupType, groups) {
  const container = $(id); container.replaceChildren();
  groups.forEach((group, groupIndex) => {
    const section = document.createElement('section');
    const heading = document.createElement('h3'); heading.textContent = group.name;
    const list = document.createElement('div'); list.className = 'mart-group-list';
    group.entries.forEach((entry, entryIndex) => {
      const row = document.createElement('div'); row.className = 'mart-row';
      const label = document.createElement('b'); label.textContent = `#${String(entryIndex + 1).padStart(2, '0')}`;
      row.append(label, selectItem(groupType, groupIndex, entryIndex, entry.item), priceInput(groupType, groupIndex, entryIndex, entry.price));
      list.append(row);
    });
    section.append(heading, list); container.append(section);
  });
}
function render() {
  renderGroups('regular', 'regular', table.regular);
  renderGroups('battle-points', 'battlePoints', table.battlePoints);
  $('warning').textContent = table.warning; ui();
}
async function load() {
  try {
    game = await post('/api/workspace/inspect', { workspacePath: $('workspace').value });
    $('open').disabled = false; $('editor').classList.remove('is-disabled'); $('status').textContent = `${game.gameVersion} cargado.`;
  } catch (error) { $('status').textContent = error.message; }
  ui();
}
async function open() {
  try { table = await post('/api/editors/marts/table', { workspacePath: $('workspace').value, language: 1 }); original = structuredClone(table); render(); }
  catch (error) { $('result').textContent = error.message; }
}
$('browse').onclick = async () => { try { $('workspace').value = (await post('/api/workspace/pick')).path; await load(); } catch (error) { $('status').textContent = error.message; } };
$('load').onclick = load; $('open').onclick = open;
function update(event) {
  const { groupType, group, entry, field } = event.target.dataset;
  if (groupType === undefined || group === undefined || entry === undefined || field === undefined) return;
  table[groupType][+group].entries[+entry][field] = +event.target.value; ui();
}
$('editor').oninput = update; $('editor').onchange = update;
$('export').onclick = async () => {
  try { const output = await post('/api/workspace/pick-output'); const result = await post('/api/editors/marts/export', { workspacePath: $('workspace').value, outputDirectory: output.path, titleId: game.titleId, regular: table.regular, battlePoints: table.battlePoints, language: 1 }); original = structuredClone(table); $('result').textContent = `Listo. ZIP: ${result.zipPath}`; ui(); }
  catch (error) { $('result').textContent = error.message; }
};
ui();
