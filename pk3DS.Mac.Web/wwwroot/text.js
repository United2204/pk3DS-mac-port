const $ = (id) => document.getElementById(id);
let game = null;
let catalog = [];
let lines = [];
let originalLines = [];
let selectedLine = -1;
let tableLoaded = false;
let recoveryErrata = [];
let recoveryTables = null;

const lineActions = document.createElement('div');
lineActions.className = 'line-actions';
lineActions.style.cssText = 'display:flex;flex-wrap:wrap;gap:.45rem;margin-top:.7rem';
for (const [id, label] of [['add-line', 'Agregar línea después'], ['remove-line', 'Eliminar línea seleccionada']]) {
  const button = document.createElement('button');
  button.id = id;
  button.type = 'button';
  button.className = 'secondary-button';
  button.textContent = label;
  lineActions.append(button);
}
document.querySelector('.line-editor-head').after(lineActions);
document.querySelectorAll('.text-archive-actions').forEach((element) => { element.style.cssText = 'display:flex;flex-wrap:wrap;align-items:center;gap:.45rem'; });

async function post(url, body = {}) {
  const response = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  const data = await response.json();
  if (!response.ok) throw new Error(data.error || data.detail || 'Operación fallida');
  return data;
}
function status(id, message, state = 'neutral') { $(id).textContent = message; $(id).className = `status ${state}`; }
function kind() { return document.querySelector('input[name="kind"]:checked').value; }
function hasChanges() { return lines.length !== originalLines.length || lines.some((line, index) => line !== originalLines[index]); }
function updateExportState() {
  const sharedLines = Math.min(lines.length, originalLines.length);
  const changed = Math.abs(lines.length - originalLines.length)
    + lines.slice(0, sharedLines).reduce((count, line, index) => count + Number(line !== originalLines[index]), 0);
  $('changes-state').textContent = changed ? `${changed} ${changed === 1 ? 'línea' : 'líneas'}` : 'Sin cambios';
  $('export-summary').textContent = changed ? `${changed} ${changed === 1 ? 'línea modificada' : 'líneas modificadas'}` : 'Sin cambios para exportar';
  $('export').disabled = !game?.titleId || !hasChanges();
  $('add-line').disabled = !tableLoaded;
  $('remove-line').disabled = !tableLoaded || selectedLine < 0;
  $('export-all-text').disabled = !game;
  $('import-all-text').disabled = !game?.titleId;
  $('randomize-text').disabled = !game?.titleId
    || ($('randomize-scope').value === 'current' && !tableLoaded);
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
  selectedLine = index; $('line-label').textContent = `Línea ${index}`; $('line-jump').value = index; $('line-value').value = lines[index]; $('line-value').disabled = false; renderLines(); updateExportState();
}
async function loadWorkspace() {
  const button = $('load'); button.disabled = true; tableLoaded = false; status('workspace-status', 'Leyendo las tablas de texto…');
  try {
    const [inspection, textCatalog] = await Promise.all([
      post('/api/workspace/inspect', { workspacePath: $('workspace').value }),
      post('/api/editors/text/catalog', { workspacePath: $('workspace').value, kind: kind(), language: 2 }),
    ]);
    game = inspection; catalog = textCatalog.tables;
    const table = $('table'); table.replaceChildren();
    for (const item of catalog) { const option = document.createElement('option'); option.value = item.index; option.textContent = `${String(item.index).padStart(3, '0')} · ${item.name} (${item.lineCount})`; table.append(option); }
    $('text-editor').classList.remove('is-disabled'); table.disabled = false; $('open-table').disabled = false;
    status('workspace-status', `${inspection.gameVersion} cargado. ${inspection.titleId ? 'Title ID detectado.' : 'Falta exheader.bin: podrás editar, pero no exportar.'}`, inspection.titleId ? 'success' : 'error');
    status('export-status', 'Abre una tabla para empezar a editar.');
  } catch (error) { game = null; tableLoaded = false; status('workspace-status', error.message, 'error'); }
  finally { button.disabled = false; updateExportState(); }
}
async function openTable() {
  const button = $('open-table'); button.disabled = true; tableLoaded = false; updateExportState(); status('export-status', 'Cargando las líneas de la tabla…');
  try {
    const data = await post('/api/editors/text/table', { workspacePath: $('workspace').value, kind: kind(), tableIndex: Number($('table').value), language: 2 });
    lines = [...data.lines]; originalLines = [...data.lines]; selectedLine = -1; tableLoaded = true; $('line-filter').value = ''; $('line-filter').disabled = false; $('line-jump').disabled = false; $('line-value').value = ''; $('line-value').disabled = true; $('line-label').textContent = 'Selecciona una línea'; renderLines(); status('export-status', 'Tabla abierta. Selecciona una línea para editarla.'); updateExportState();
  } catch (error) { status('export-status', error.message, 'error'); }
  finally { button.disabled = false; }
}
function addLine() {
  if (!tableLoaded) return;
  const insertAt = selectedLine >= 0 ? selectedLine + 1 : lines.length;
  if (selectedLine > 0 && selectedLine < lines.length - 1
      && !window.confirm('Insertar una línea aquí desplazará las líneas siguientes. ¿Continuar?')) return;
  lines.splice(insertAt, 0, '');
  selectLine(insertAt);
  updateExportState();
}
function removeLine() {
  if (!tableLoaded || selectedLine < 0) return;
  if (selectedLine < lines.length - 1
      && !window.confirm('Eliminar esta línea desplazará las líneas siguientes. ¿Continuar?')) return;
  lines.splice(selectedLine, 1);
  if (lines.length) selectLine(Math.min(selectedLine, lines.length - 1));
  else {
    selectedLine = -1;
    $('line-label').textContent = 'Sin línea seleccionada';
    $('line-jump').value = '';
    $('line-value').value = '';
    $('line-value').disabled = true;
    renderLines();
  }
  updateExportState();
}
$('browse').addEventListener('click', async () => { try { const data = await post('/api/workspace/pick'); $('workspace').value = data.path; await loadWorkspace(); } catch (error) { status('workspace-status', error.message, 'neutral'); } });
$('load').addEventListener('click', loadWorkspace);
document.querySelectorAll('input[name="kind"]').forEach((control) => control.addEventListener('change', () => { catalog = []; lines = []; originalLines = []; selectedLine = -1; tableLoaded = false; if (game) loadWorkspace(); updateExportState(); }));
$('open-table').addEventListener('click', openTable);
$('add-line').addEventListener('click', addLine);
$('remove-line').addEventListener('click', removeLine);
$('export-all-text').addEventListener('click', exportAllText);
$('import-all-text').addEventListener('click', () => $('import-text-file').click());
$('import-text-file').addEventListener('change', importAllText);
$('randomize-scope').addEventListener('change', updateExportState);
$('randomize-text').addEventListener('click', randomizeText);
$('line-filter').addEventListener('input', renderLines);
$('line-list').addEventListener('click', (event) => { const button = event.target.closest('button[data-line]'); if (button) selectLine(Number(button.dataset.line)); });
$('line-jump').addEventListener('change', () => selectLine(Number($('line-jump').value)));
$('line-value').addEventListener('input', () => { if (selectedLine < 0) return; lines[selectedLine] = $('line-value').value; renderLines(); updateExportState(); });
$('export').addEventListener('click', async () => {
  const button = $('export'); button.disabled = true; status('export-status', 'Elige una carpeta para guardar el LayeredFS…');
  try { const output = await post('/api/workspace/pick-output'); status('export-status', 'Empaquetando la tabla modificada…'); const data = await post('/api/editors/text/export', { workspacePath: $('workspace').value, outputDirectory: output.path, titleId: game.titleId, kind: kind(), tableIndex: Number($('table').value), lines, language: 2 }); originalLines = [...lines]; renderLines(); status('export-status', `Listo. ZIP: ${data.zipPath}`, 'success'); }
  catch (error) { status('export-status', error.message, 'error'); }
  finally { updateExportState(); }
});

function buildTextDump(tables) {
  const output = [];
  for (let index = 0; index < tables.length; index++) {
    output.push('~~~~~~~~~~~~~~~', 'Text File : ' + index, '~~~~~~~~~~~~~~~', ...tables[index]);
  }
  return output.join('\r\n') + '\r\n';
}
function encodeUtf16Le(value) {
  const bytes = new Uint8Array(2 + value.length * 2);
  bytes[0] = 0xff;
  bytes[1] = 0xfe;
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index);
    bytes[2 + index * 2] = code & 0xff;
    bytes[3 + index * 2] = code >>> 8;
  }
  return new Blob([bytes], { type: 'text/plain;charset=utf-16le' });
}
function downloadUtf16Text(filename, content) {
  const url = URL.createObjectURL(encodeUtf16Le(content));
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.append(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}
function downloadUtf8Text(filename, content) {
  const url = URL.createObjectURL(new Blob([content], { type: 'text/plain;charset=utf-8' }));
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.append(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}
function downloadTextDump(tables, filenameOverride = null, showStatus = true) {
  const filename = filenameOverride || kind().toLowerCase() + '-' + game.gameVersion.toLowerCase() + '.txt';
  const url = URL.createObjectURL(encodeUtf16Le(buildTextDump(tables)));
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.append(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
  if (showStatus) status('export-status', 'TXT UTF-16LE descargado: ' + filename, 'success');
}
function setTextRecovery(result, tables) {
  const failed = result.failedTableIndices || [];
  recoveryErrata = result.errata || [];
  recoveryTables = failed.length ? tables : null;
  $('text-recovery-downloads').hidden = failed.length === 0;
  if (!failed.length) return '';
  const indices = failed.map((index) => String(index).padStart(3, '0')).join(', ');
  return ` Falló la conversión de las tablas ${indices}; se conservaron sus originales y las demás se exportaron. Descargá los informes adjuntos.`;
}
function clearTextRecovery() {
  recoveryErrata = [];
  recoveryTables = null;
  $('text-recovery-downloads').hidden = true;
}
function downloadTextRecoveryErrata() {
  if (!recoveryErrata.length) return;
  downloadUtf8Text('text_errata.txt', recoveryErrata.join('\r\n') + '\r\n');
}
function downloadTextRecoveryFailed() {
  if (!recoveryTables) return;
  downloadTextDump(recoveryTables, 'text_failed.txt', false);
}
async function exportAllText() {
  const button = $('export-all-text');
  button.disabled = true;
  status('export-status', 'Leyendo todas las tablas de texto…');
  try {
    const data = await post('/api/editors/text/all', { workspacePath: $('workspace').value, kind: kind(), language: 2 });
    downloadTextDump(data.tables);
  } catch (error) {
    status('export-status', error.message, 'error');
  } finally {
    updateExportState();
  }
}
async function readUtf16File(file) {
  return await new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result || ''));
    reader.onerror = () => reject(new Error('No se pudo leer el archivo TXT.'));
    reader.readAsText(file, 'UTF-16LE');
  });
}
function parseTextDump(content, expectedTables) {
  const separator = '~~~~~~~~~~~~~~~';
  const fileLines = content.replace(/^\uFEFF/, '').split(/\r\n|\n|\r/);
  if (fileLines.at(-1) === '') fileLines.pop();
  const tables = [];
  let cursor = 0;
  for (let index = 0; index < expectedTables; index++) {
    while (cursor < fileLines.length && fileLines[cursor] !== separator) cursor++;
    if (cursor + 2 >= fileLines.length
        || fileLines[cursor + 1] !== 'Text File : ' + index
        || fileLines[cursor + 2] !== separator) {
      throw new Error('El TXT debe contener la cabecera Text File : ' + index + ' en el orden esperado.');
    }
    cursor += 3;
    const table = [];
    while (cursor < fileLines.length && fileLines[cursor] !== separator) table.push(fileLines[cursor++]);
    tables.push(table);
  }
  if (fileLines.slice(cursor).includes(separator))
    throw new Error('El TXT contiene más tablas que el archivo de texto seleccionado.');
  if (tables.length !== expectedTables)
    throw new Error('La cantidad de tablas no coincide: se recibieron ' + tables.length + ' y se esperaban ' + expectedTables + '.');
  if (!tables.some((table) => table.some((line) => line.includes('\\n'))))
    throw new Error('El TXT no conserva los códigos de formato \\n. Exportá el archivo sin quitar el formato de saltos de línea.');
  return tables;
}
async function importAllText(event) {
  const file = event.target.files?.[0];
  event.target.value = '';
  if (!file) return;
  const button = $('import-all-text');
  button.disabled = true;
  clearTextRecovery();
  try {
    status('export-status', 'Leyendo y validando el TXT…');
    const tables = parseTextDump(await readUtf16File(file), catalog.length);
    status('export-status', 'Elige una carpeta para guardar el LayeredFS…');
    const output = await post('/api/workspace/pick-output');
    status('export-status', 'Empaquetando todas las tablas…');
    const result = await post('/api/editors/text/export-all', {
      workspacePath: $('workspace').value,
      outputDirectory: output.path,
      titleId: game.titleId,
      kind: kind(),
      tables,
      language: 2,
    });
    catalog.forEach((item, index) => {
      item.lineCount = tables[index].length;
      const option = $('table').querySelector('option[value="' + index + '"]');
      if (option) option.textContent = String(index).padStart(3, '0') + ' · ' + item.name + ' (' + item.lineCount + ')';
    });
    const currentIndex = Number($('table').value);
    if (tableLoaded && Number.isInteger(currentIndex) && tables[currentIndex]) {
      lines = [...tables[currentIndex]];
      originalLines = [...lines];
      selectedLine = -1;
      $('line-value').value = '';
      $('line-value').disabled = true;
      $('line-label').textContent = 'Selecciona una línea';
      renderLines();
    }
    const recoveryNote = setTextRecovery(result, tables);
    status('export-status', 'Listo. ZIP: ' + result.zipPath + '.' + recoveryNote,
      recoveryNote ? 'warning' : 'success');
  } catch (error) {
    status('export-status', error.message, 'error');
  } finally {
    updateExportState();
  }
}
function shuffle(values) {
  for (let index = values.length - 1; index > 0; index--) {
    const swap = Math.floor(Math.random() * (index + 1));
    [values[index], values[swap]] = [values[swap], values[index]];
  }
}
function shuffleTextTables(tables, smart) {
  const pool = [];
  for (const table of tables) {
    for (const line of table) {
      if (!smart || !line.includes('[')) pool.push(line);
    }
  }
  if (pool.length === 0) throw new Error('No hay líneas elegibles para randomizar con el modo seleccionado.');
  shuffle(pool);
  let cursor = 0;
  for (const table of tables) {
    for (let index = 0; index < table.length; index++) {
      if (!smart || !table[index].includes('[')) table[index] = pool[cursor++];
    }
  }
}
async function randomizeText() {
  if (kind() === 'Game'
      && !window.confirm('Randomizar Game Text puede romper el formato y causar errores en el juego. ¿Continuar?')) return;
  const button = $('randomize-text');
  button.disabled = true;
  const all = $('randomize-scope').value === 'all';
  const smart = $('smart-shuffle').checked;
  clearTextRecovery();
  try {
    status('export-status', all ? 'Cargando todas las tablas para randomizar…' : 'Preparando la tabla abierta…');
    let tables;
    const tableIndex = Number($('table').value);
    if (all) {
      tables = (await post('/api/editors/text/all', { workspacePath: $('workspace').value, kind: kind(), language: 2 })).tables;
      if (tableLoaded && tables[tableIndex]) tables[tableIndex] = [...lines];
    } else {
      if (!tableLoaded) throw new Error('Abre una tabla antes de randomizar solo esa tabla.');
      tables = [[...lines]];
    }
    shuffleTextTables(tables, smart);
    status('export-status', 'Elige una carpeta para guardar el LayeredFS…');
    const output = await post('/api/workspace/pick-output');
    status('export-status', 'Empaquetando el texto randomizado…');
    const result = all
      ? await post('/api/editors/text/export-all', {
        workspacePath: $('workspace').value, outputDirectory: output.path, titleId: game.titleId,
        kind: kind(), tables, language: 2,
      })
      : await post('/api/editors/text/export', {
        workspacePath: $('workspace').value, outputDirectory: output.path, titleId: game.titleId,
        kind: kind(), tableIndex, lines: tables[0], language: 2,
      });
    if (all && tableLoaded && tables[tableIndex]) {
      lines = [...tables[tableIndex]];
      originalLines = [...lines];
      selectedLine = -1;
      $('line-value').value = '';
      $('line-value').disabled = true;
      $('line-label').textContent = 'Selecciona una línea';
      renderLines();
    } else if (!all) {
      lines = [...tables[0]];
      originalLines = [...lines];
      selectedLine = -1;
      $('line-value').value = '';
      $('line-value').disabled = true;
      $('line-label').textContent = 'Selecciona una línea';
      renderLines();
    }
    const recoveryNote = all ? setTextRecovery(result, tables) : '';
    status('export-status', 'Listo. ZIP: ' + result.zipPath + '.' + recoveryNote,
      recoveryNote ? 'warning' : 'success');
  } catch (error) {
    status('export-status', error.message, 'error');
  } finally {
    updateExportState();
  }
}
$('download-text-errata').addEventListener('click', downloadTextRecoveryErrata);
$('download-text-failed').addEventListener('click', downloadTextRecoveryFailed);
updateExportState();
