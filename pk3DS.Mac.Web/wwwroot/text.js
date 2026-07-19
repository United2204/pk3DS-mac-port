const $ = (id) => document.getElementById(id);
let game = null;
let catalog = [];
let lines = [];
let originalLines = [];
let selectedLine = -1;

async function post(url, body = {}) {
  const response = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  const data = await response.json();
  if (!response.ok) throw new Error(data.error || data.detail || 'Operación fallida');
  return data;
}
function status(id, message, state = 'neutral') { $(id).textContent = message; $(id).className = `status ${state}`; }
function kind() { return document.querySelector('input[name="kind"]:checked').value; }
function hasChanges() { return lines.some((line, index) => line !== originalLines[index]); }
function updateExportState() {
  const changed = lines.reduce((count, line, index) => count + Number(line !== originalLines[index]), 0);
  $('changes-state').textContent = changed ? `${changed} ${changed === 1 ? 'línea' : 'líneas'}` : 'Sin cambios';
  $('export-summary').textContent = changed ? `${changed} ${changed === 1 ? 'línea modificada' : 'líneas modificadas'}` : 'Sin cambios para exportar';
  $('export').disabled = !game?.titleId || !hasChanges();
}
function renderLines() {
  const search = $('line-filter').value.trim().toLocaleLowerCase();
  const matches = [];
  lines.forEach((line, index) => { if (!search || line.toLocaleLowerCase().includes(search) || String(index) === search) matches.push(index); });
  const visible = matches.slice(0, 500);
  $('line-count').textContent = matches.length > 500 ? `${matches.length} coincidencias; se muestran las primeras 500.` : `${matches.length} ${matches.length === 1 ? 'línea' : 'líneas'}.`;
  const list = $('line-list'); list.replaceChildren();
  for (const index of visible) {
    const button = document.createElement('button'); button.type = 'button'; button.className = `line-row${index === selectedLine ? ' is-selected' : ''}${lines[index] !== originalLines[index] ? ' is-changed' : ''}`;
    button.dataset.line = index; button.innerHTML = `<b>${index}</b><span></span>`; button.querySelector('span').textContent = lines[index] || '—'; list.append(button);
  }
}
function selectLine(index) {
  if (!Number.isInteger(index) || index < 0 || index >= lines.length) return;
  selectedLine = index; $('line-label').textContent = `Línea ${index}`; $('line-jump').value = index; $('line-value').value = lines[index]; $('line-value').disabled = false; renderLines();
}
async function loadWorkspace() {
  const button = $('load'); button.disabled = true; status('workspace-status', 'Leyendo las tablas de texto…');
  try {
    const [inspection, textCatalog] = await Promise.all([
      post('/api/workspace/inspect', { workspacePath: $('workspace').value }),
      post('/api/editors/text/catalog', { workspacePath: $('workspace').value, kind: kind(), language: 1 }),
    ]);
    game = inspection; catalog = textCatalog.tables;
    const table = $('table'); table.replaceChildren();
    for (const item of catalog) { const option = document.createElement('option'); option.value = item.index; option.textContent = `${String(item.index).padStart(3, '0')} · ${item.name} (${item.lineCount})`; table.append(option); }
    $('text-editor').classList.remove('is-disabled'); table.disabled = false; $('open-table').disabled = false;
    status('workspace-status', `${inspection.gameVersion} cargado. ${inspection.titleId ? 'Title ID detectado.' : 'Falta exheader.bin: podrás editar, pero no exportar.'}`, inspection.titleId ? 'success' : 'error');
    status('export-status', 'Abre una tabla para empezar a editar.');
  } catch (error) { game = null; status('workspace-status', error.message, 'error'); }
  finally { button.disabled = false; updateExportState(); }
}
async function openTable() {
  const button = $('open-table'); button.disabled = true; status('export-status', 'Cargando las líneas de la tabla…');
  try {
    const data = await post('/api/editors/text/table', { workspacePath: $('workspace').value, kind: kind(), tableIndex: Number($('table').value), language: 1 });
    lines = [...data.lines]; originalLines = [...data.lines]; selectedLine = -1; $('line-filter').value = ''; $('line-filter').disabled = false; $('line-jump').disabled = false; $('line-value').value = ''; $('line-value').disabled = true; $('line-label').textContent = 'Selecciona una línea'; renderLines(); status('export-status', 'Tabla abierta. Selecciona una línea para editarla.'); updateExportState();
  } catch (error) { status('export-status', error.message, 'error'); }
  finally { button.disabled = false; }
}
$('browse').addEventListener('click', async () => { try { const data = await post('/api/workspace/pick'); $('workspace').value = data.path; await loadWorkspace(); } catch (error) { status('workspace-status', error.message, 'neutral'); } });
$('load').addEventListener('click', loadWorkspace);
document.querySelectorAll('input[name="kind"]').forEach((control) => control.addEventListener('change', () => { catalog = []; lines = []; originalLines = []; selectedLine = -1; if (game) loadWorkspace(); }));
$('open-table').addEventListener('click', openTable);
$('line-filter').addEventListener('input', renderLines);
$('line-list').addEventListener('click', (event) => { const button = event.target.closest('button[data-line]'); if (button) selectLine(Number(button.dataset.line)); });
$('line-jump').addEventListener('change', () => selectLine(Number($('line-jump').value)));
$('line-value').addEventListener('input', () => { if (selectedLine < 0) return; lines[selectedLine] = $('line-value').value; renderLines(); updateExportState(); });
$('export').addEventListener('click', async () => {
  const button = $('export'); button.disabled = true; status('export-status', 'Elige una carpeta para guardar el LayeredFS…');
  try { const output = await post('/api/workspace/pick-output'); status('export-status', 'Empaquetando la tabla modificada…'); const data = await post('/api/editors/text/export', { workspacePath: $('workspace').value, outputDirectory: output.path, titleId: game.titleId, kind: kind(), tableIndex: Number($('table').value), lines, language: 1 }); originalLines = [...lines]; renderLines(); status('export-status', `Listo. ZIP: ${data.zipPath}`, 'success'); }
  catch (error) { status('export-status', error.message, 'error'); }
  finally { updateExportState(); }
});
updateExportState();
