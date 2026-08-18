import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { createRoot } from "react-dom/client";
import {
  BrowserRouter,
  Link,
  NavLink,
  Outlet,
  Route,
  Routes,
  useLocation,
  useParams,
  useSearchParams,
} from "react-router-dom";
import "./styles.css";

const WORKSPACE_KEY = "pk3ds:workspace:v2";
const FRAME_STATE_PREFIX = "pk3ds:frame:v1:";

const editorPages = [
  { id: "personal", label: "Personal Stats", legacy: "personal.html", group: "RomFS", description: "Habilidades, tipos, estadísticas y compatibilidades." },
  { id: "moves", label: "Move Stats", legacy: "moves.html", group: "RomFS", description: "Datos y propiedades de los movimientos." },
  { id: "items", label: "Item Stats", legacy: "items.html", group: "RomFS", description: "Objetos, efectos y valores de compra." },
  { id: "levelup", label: "Level Up Moves", legacy: "levelup.html", group: "RomFS", description: "Movimientos aprendidos por nivel." },
  { id: "eggmoves", availabilityId: "eggmove", label: "Egg Moves", legacy: "eggmoves.html", group: "RomFS", description: "Movimientos heredados mediante crianza." },
  { id: "evolutions", label: "Evolutions", legacy: "evolutions.html", group: "RomFS", description: "Métodos y especies resultantes." },
  { id: "wild", label: "Wild Encounters", legacy: "wild.html", group: "RomFS", description: "Encuentros salvajes por área." },
  { id: "static", label: "Static Encounters", legacy: "static.html", group: "RomFS", description: "Regalos y encuentros fijos." },
  { id: "trainers", label: "Trainers", legacy: "trainers.html", group: "RomFS", description: "Entrenadores, equipos y nombres." },
  { id: "text", label: "Game / Story Text", legacy: "text.html", group: "RomFS", description: "Tablas de texto del juego." },
  { id: "mega", label: "Mega Evolutions", legacy: "mega.html", group: "RomFS", description: "Datos de megaevolución." },
  { id: "owse", label: "OWSE / Scripts", legacy: "owse.html", group: "RomFS", description: "Inspección de scripts y zonas." },
  { id: "tm", label: "TMs / HMs", legacy: "tm.html", group: "ExeFS", description: "Compatibilidad y movimientos de campo." },
  { id: "tutors", label: "Move Tutors", legacy: "tutors.html", group: "ExeFS", description: "Tutores de juegos Gen. VII." },
  { id: "tutors6", label: "Move Tutors Gen. VI", legacy: "tutors6.html", group: "ExeFS", description: "Tutores de X/Y y OR/AS." },
  { id: "marts", label: "Poké Mart", legacy: "marts.html", group: "ExeFS", description: "Inventarios de tiendas Gen. VII." },
  { id: "marts6", label: "Poké Mart Gen. VI", legacy: "marts6.html", group: "ExeFS", description: "Inventarios de tiendas Gen. VI." },
  { id: "opowers", label: "O-Powers", legacy: "opowers.html", group: "ExeFS", description: "Valores de O-Powers." },
  { id: "shiny-rate", label: "Shiny Rate", legacy: "shiny-rate.html", group: "ExeFS", description: "Probabilidades y rerolls shiny." },
  { id: "pickup", label: "Pickup", legacy: "pickup.html", group: "ExeFS", description: "Objetos obtenidos mediante Pickup." },
  { id: "pickup6", label: "Pickup Gen. VI", legacy: "pickup6.html", group: "ExeFS", description: "Tablas de Pickup de X/Y y OR/AS." },
  { id: "typechart", label: "Type Chart", legacy: "typechart.html", group: "CRO", description: "Matriz de efectividad de tipos." },
  { id: "starters", availabilityId: "starter", label: "Starter Pokémon", legacy: "starters.html", group: "CRO", description: "Pokémon iniciales de Gen. VI." },
  { id: "gifts6", availabilityId: "gift6", label: "Gift Pokémon", legacy: "gifts6.html", group: "CRO", description: "Pokémon recibidos durante la historia." },
  { id: "maison", label: "Maison / Tree / Royal", legacy: "maison.html", group: "CRO", description: "Equipos de Battle Maison, Tree y Royal." },
];

const projectTools = [
  { id: "extract", label: "Extraer juego", section: "extract", eyebrow: "ENTRADA", description: "Convertí un CXI o 3DS en un workspace editable." },
  { id: "inspect", label: "Validar workspace", section: "inspect", eyebrow: "DIAGNÓSTICO", description: "Comprobá RomFS, ExeFS, exheader y módulos disponibles." },
  { id: "build", label: "Construir archivos", section: "build", eyebrow: "SALIDA", description: "Generá romfs.bin y exefs.bin sin tocar el origen." },
  { id: "rebuild", label: "Reconstruir ROM 3DS", section: "rebuild-rom", eyebrow: "EMPAQUETADO", description: "Armá una ROM recortada o con padding de tarjeta." },
  { id: "cia", label: "Crear un CIA", section: "rebuild-cia", eyebrow: "EMPAQUETADO", description: "Convertí una ROM mediante makerom externo." },
  { id: "patch", label: "Crear parche LayeredFS", section: "patch", eyebrow: "PARCHE", description: "Prepará el contenido redirigido para Luma." },
  { id: "archives", label: "GARC / DARC / SARC / FARC", section: "garc", eyebrow: "FORMATOS", description: "Desempaquetá y empaquetá archivos internos." },
  { id: "titlescreen", games: ["XY", "ORAS"], label: "Pantalla de título", section: "titlescreen", eyebrow: "RECURSOS", description: "Inventariá, previsualizá y reemplazá imágenes." },
];

const groups = [
  {
    id: "project",
    label: "Proyecto",
    icon: "▣",
    links: [{ to: "/project", label: "Herramientas de proyecto", description: "Extraer, construir y empaquetar" }],
  },
  {
    id: "romfs",
    label: "RomFS",
    icon: "◈",
    links: editorPages.filter((page) => page.group === "RomFS").map((page) => ({ to: `/editor/${page.id}`, label: page.label, description: page.description })),
  },
  {
    id: "exefs",
    label: "ExeFS",
    icon: "◉",
    links: editorPages.filter((page) => page.group === "ExeFS").map((page) => ({ to: `/editor/${page.id}`, label: page.label, description: page.description })),
  },
  {
    id: "cro",
    label: "CRO / Salida",
    icon: "◆",
    links: editorPages.filter((page) => page.group === "CRO").map((page) => ({ to: `/editor/${page.id}`, label: page.label, description: page.description })),
  },
];

function readJson(key, fallback) {
  try {
    const raw = localStorage.getItem(key);
    return raw ? { ...fallback, ...JSON.parse(raw) } : fallback;
  } catch {
    return fallback;
  }
}

const defaultWorkspace = {
  path: "",
  gameVersion: "",
  titleId: "",
  isComplete: false,
  hasExeFs: false,
  hasExheader: false,
  modules: [],
  status: "Seleccioná una carpeta extraída para comenzar.",
  inspectedAt: null,
};

const WorkspaceContext = createContext(null);

function WorkspaceProvider({ children }) {
  const [workspace, setWorkspace] = useState(() => {
    const saved = readJson(WORKSPACE_KEY, defaultWorkspace);
    return saved.isComplete && saved.gameVersion
      ? { ...saved, status: `RomFS válida: ${saved.gameVersion}.` }
      : saved;
  });

  useEffect(() => {
    localStorage.setItem(WORKSPACE_KEY, JSON.stringify(workspace));
  }, [workspace]);

  const setPath = useCallback((path) => {
    setWorkspace((current) => ({
      ...current,
      path,
      gameVersion: "",
      titleId: "",
      isComplete: false,
      hasExeFs: false,
      hasExheader: false,
      modules: [],
      inspectedAt: null,
      status: path ? "Listo para validar." : defaultWorkspace.status,
    }));
  }, []);

  const inspect = useCallback(async () => {
    if (!workspace.path.trim()) {
      setWorkspace((current) => ({ ...current, status: "Elegí una carpeta antes de validar." }));
      return false;
    }

    setWorkspace((current) => ({ ...current, status: "Validando workspace…" }));
    try {
      const response = await fetch("/api/workspace/inspect", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ workspacePath: workspace.path.trim() }),
      });
      const body = await response.json();
      if (!response.ok) throw new Error(body.message || body.title || "No se pudo validar el workspace.");
      setWorkspace((current) => ({
        ...current,
        gameVersion: body.gameVersion || "",
        titleId: body.titleId || "",
        isComplete: Boolean(body.isComplete),
        hasExeFs: Boolean(body.exeFsPath),
        hasExheader: Boolean(body.exheaderPath),
        modules: Array.isArray(body.modules) ? body.modules : [],
        status: body.isComplete ? `RomFS válida: ${body.gameVersion}.` : "La RomFS no es válida.",
        inspectedAt: new Date().toISOString(),
      }));
      return true;
    } catch (error) {
      setWorkspace((current) => ({ ...current, gameVersion: "", titleId: "", isComplete: false, hasExeFs: false, hasExheader: false, modules: [], status: error.message }));
      return false;
    }
  }, [workspace.path]);

  const pickFolder = useCallback(async () => {
    try {
      const response = await fetch("/api/workspace/pick", { method: "POST" });
      const body = await response.json();
      if (body.path) setPath(body.path);
    } catch (error) {
      setWorkspace((current) => ({ ...current, status: error.message }));
    }
  }, [setPath]);

  const moduleInfo = useCallback((moduleId) => {
    if (!moduleId) return null;
    return workspace.modules.find((module) => module.id === moduleId) || null;
  }, [workspace.modules]);

  const moduleAvailable = useCallback((moduleId) => {
    if (!workspace.isComplete) return false;
    if (!moduleId || workspace.modules.length === 0) return true;
    return Boolean(moduleInfo(moduleId)?.sourceAvailable);
  }, [moduleInfo, workspace.isComplete, workspace.modules.length]);

  const value = useMemo(() => ({ workspace, setPath, inspect, pickFolder, moduleInfo, moduleAvailable }), [workspace, setPath, inspect, pickFolder, moduleInfo, moduleAvailable]);
  return <WorkspaceContext.Provider value={value}>{children}</WorkspaceContext.Provider>;
}

function useWorkspace() {
  const context = useContext(WorkspaceContext);
  if (!context) throw new Error("useWorkspace debe usarse dentro de WorkspaceProvider");
  return context;
}

function App() {
  return (
    <WorkspaceProvider>
      <BrowserRouter basename="/app">
        <Routes>
          <Route element={<AppShell />}>
            <Route index element={<Dashboard />} />
            <Route path="randomizer" element={<RandomizerPage />} />
            <Route path="project" element={<ProjectPage />} />
            <Route path="project/tools" element={<ProjectToolsPage />} />
            <Route path="editor/:id" element={<EditorPage />} />
            <Route path="category/:group" element={<CategoryPage />} />
            <Route path="*" element={<NotFound />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </WorkspaceProvider>
  );
}

function AppShell() {
  const location = useLocation();
  const [menuOpen, setMenuOpen] = useState(false);
  const { workspace, moduleAvailable } = useWorkspace();
  const activePage = editorPages.find((page) => location.pathname === `/app/editor/${page.id}` || location.pathname === `/editor/${page.id}`);

  useEffect(() => setMenuOpen(false), [location.pathname]);

  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="brand-lockup">
          <Link className="brand" to="/">pk3DS <b>Mac</b></Link>
          <span className="brand-subtitle">Editor de Pokémon 3DS</span>
        </div>
        <div className="header-workspace">
          <span className={`workspace-dot ${workspace.isComplete ? "is-ready" : ""}`} aria-hidden="true" />
          <span className="workspace-name">{workspace.gameVersion || "Sin workspace"}</span>
          {workspace.titleId && <code>{workspace.titleId}</code>}
        </div>
        <button className="menu-button" type="button" aria-expanded={menuOpen} onClick={() => setMenuOpen((open) => !open)}>
          <span aria-hidden="true">☰</span> Menú
        </button>
      </header>

      <div className="workspace-global">
        <WorkspaceCard global />
      </div>

      {menuOpen && (
        <>
          <button className="menu-backdrop" aria-label="Cerrar menú" type="button" onClick={() => setMenuOpen(false)} />
          <nav className="app-menu" aria-label="Navegación principal">
            <div className="app-menu-head"><span className="kicker">NAVEGACIÓN</span><b>{activePage?.label || "pk3DS Mac"}</b></div>
            <NavLink to="/" end className="menu-home"><span>⌂</span> Inicio</NavLink>
            <NavLink to="/randomizer" className="menu-home"><span>↻</span> Randomizador</NavLink>
            {groups.map((group) => (
              <div className="menu-group" key={group.id}>
                <p><span>{group.icon}</span>{group.label}</p>
                {group.links.map((link) => {
                  const page = editorPages.find((item) => link.to === `/editor/${item.id}`);
                  const available = !page || moduleAvailable(page.availabilityId || page.id);
                  if (workspace.isComplete && !available) return null;
                  return available
                    ? <NavLink key={link.to} to={link.to} className="menu-link"><b>{link.label}</b><small>{link.description}</small></NavLink>
                    : <span key={link.to} className="menu-link is-disabled" aria-disabled="true"><b>{link.label}</b><small>Requiere un workspace válido y compatible.</small></span>;
                })}
              </div>
            ))}
          </nav>
        </>
      )}

      <div className="app-body">
        <aside className="side-nav" aria-label="Secciones">
          <p className="side-title">SECCIONES</p>
          <NavLink to="/" end className="side-link"><span>⌂</span><b>Inicio</b><small>Resumen</small></NavLink>
          <NavLink to="/randomizer" className="side-link"><span>↻</span><b>Randomizador</b><small>Opciones de juego</small></NavLink>
          <NavLink to="/project" className="side-link"><span>▣</span><b>Proyecto</b><small>Archivos y ROM</small></NavLink>
          <NavLink to="/category/RomFS" className="side-link"><span>◈</span><b>RomFS</b><small>Datos del juego</small></NavLink>
          <NavLink to="/category/ExeFS" className="side-link"><span>◉</span><b>ExeFS</b><small>Código y tablas</small></NavLink>
          <NavLink to="/category/CRO" className="side-link"><span>◆</span><b>CRO / Salida</b><small>Recursos especiales</small></NavLink>
          <div className="side-footer"><span className="workspace-dot" />Todo local en tu Mac</div>
        </aside>
        <main className="app-main"><Outlet /></main>
      </div>
    </div>
  );
}

function WorkspaceCard({ compact = false, global = false }) {
  const { workspace, setPath, inspect, pickFolder } = useWorkspace();
  const [busy, setBusy] = useState(false);

  const handleInspect = async () => {
    setBusy(true);
    await inspect();
    setBusy(false);
  };

  return (
    <section className={`workspace-card ${compact ? "is-compact" : ""} ${global ? "is-global" : ""}`}>
      <div className="card-heading"><div><p className="kicker">WORKSPACE ACTIVO</p><h2>{global ? "Juego cargado para toda la sesión" : compact ? "Juego seleccionado" : "Abrí tu juego extraído"}</h2></div><span className={`status-chip ${workspace.isComplete ? "is-ready" : ""}`}>{workspace.isComplete ? "RomFS válida" : "Pendiente"}</span></div>
      <p className="muted">{workspace.isComplete ? "Este workspace se comparte entre todas las funciones de la sesión." : "Cargá aquí la carpeta extraída. Las funciones se habilitan según la validez y los archivos disponibles."}</p>
      <div className="workspace-input"><span aria-hidden="true">⌁</span><input value={workspace.path} onChange={(event) => setPath(event.target.value)} placeholder="/Users/.../X-extracted" spellCheck="false" /><button className="button secondary" type="button" onClick={pickFolder}>Examinar…</button><button className="button primary" type="button" onClick={handleInspect} disabled={busy}>{busy ? "Validando…" : "Cargar"}</button></div>
      <div className={`workspace-status ${workspace.isComplete ? "is-ready" : ""}`} role="status"><span aria-hidden="true">{workspace.isComplete ? "✓" : "i"}</span>{workspace.status}</div>
      {workspace.isComplete && <div className="workspace-meta"><span><b>Juego</b>{workspace.gameVersion}</span><span><b>RomFS</b>Detectada</span><span><b>ExeFS</b>{workspace.hasExeFs ? "Detectado" : "No detectado"}</span><span><b>exheader</b>{workspace.hasExheader ? "Detectado" : "No detectado"}</span><span><b>Title ID</b><code>{workspace.titleId || "No detectado"}</code></span><span><b>Última validación</b>{workspace.inspectedAt ? new Date(workspace.inspectedAt).toLocaleString("es-UY") : "Ahora"}</span></div>}
    </section>
  );
}

function Dashboard() {
  return (
    <div className="page dashboard-page">
      <div className="page-intro"><p className="eyebrow">INICIO</p><h1>Tu proyecto Pokémon</h1><p>Elegí un workspace y trabajá con la misma lógica de categorías que pk3DS para Windows.</p></div>
      <section className="windows-tabs" aria-label="Áreas de trabajo">
        <div className="section-title"><div><p className="kicker">ÁREAS DE TRABAJO</p><h2>¿Qué querés hacer?</h2></div><span className="muted">La selección se conserva al cambiar de sección.</span></div>
        <div className="tab-grid">
          <Link className="tab-card is-featured" to="/randomizer"><span className="tab-icon">↻</span><div><b>Randomizador</b><small>Personalizá datos, movimientos y evoluciones</small></div><span className="arrow">→</span></Link>
          <Link className="tab-card is-featured" to="/project"><span className="tab-icon">▣</span><div><b>Herramientas de proyecto</b><small>Extraer, validar, construir y reconstruir</small></div><span className="arrow">→</span></Link>
          <Link className="tab-card" to="/category/RomFS"><span className="tab-icon">◈</span><div><b>RomFS</b><small>Personal, movimientos, encuentros y texto</small></div><span className="arrow">→</span></Link>
          <Link className="tab-card" to="/category/ExeFS"><span className="tab-icon">◉</span><div><b>ExeFS</b><small>TMs, tutores, tiendas y código</small></div><span className="arrow">→</span></Link>
          <Link className="tab-card" to="/category/CRO"><span className="tab-icon">◆</span><div><b>CRO / Salida</b><small>Regalos, starters, tipos y Maison</small></div><span className="arrow">→</span></Link>
        </div>
      </section>
      <section className="resume-card"><div><p className="kicker">SESIÓN</p><h2>Podés cerrar y volver después</h2><p>La carpeta, el juego detectado y los campos de las herramientas se guardan localmente. Los archivos generados siguen en el workspace o carpeta de salida.</p></div><span className="resume-icon">↻</span></section>
    </div>
  );
}

function ProjectPage() {
  const { workspace } = useWorkspace();
  const standaloneTools = new Set(["extract", "inspect", "archives"]);
  const visibleTools = workspace.isComplete
    ? projectTools.filter((tool) => !tool.games || tool.games.includes(workspace.gameVersion))
    : projectTools;

  return (
    <div className="page">
      <div className="page-intro"><p className="eyebrow">PROYECTO</p><h1>Herramientas de proyecto</h1><p>El flujo de Windows, dividido en pasos claros para trabajar con tu dump sin modificar el origen.</p></div>
      <section className="tool-board"><div className="section-title"><div><p className="kicker">FLUJO PRINCIPAL</p><h2>Elegí una tarea</h2></div><Link className="text-link" to="/project/tools?focus=extract">Abrir vista completa →</Link></div><div className="tool-grid">{visibleTools.map((tool, index) => {
        const available = projectToolAvailable(tool, workspace, standaloneTools);
        const disabledReason = !workspace.isComplete
          ? "Cargá y validá el workspace para habilitarla."
          : tool.id === "patch"
            ? "Requiere ExeFS con code.bin descomprimido."
            : tool.id === "rebuild" || tool.id === "cia"
              ? "Requiere ExeFS y exheader.bin."
              : "No disponible para este juego.";
        const content = <><span className="tool-step">{index + 1}</span><div><p className="kicker">{tool.eyebrow}</p><h3>{tool.label}</h3><p>{tool.description}</p>{!available && <small className="disabled-note">{disabledReason}</small>}</div><span className="arrow">→</span></>;
        return available
          ? <Link className={`tool-card ${index < 3 ? "is-primary" : ""}`} to={`/project/tools?focus=${tool.id}`} key={tool.id}>{content}</Link>
          : <div className={`tool-card is-disabled ${index < 3 ? "is-primary" : ""}`} aria-disabled="true" key={tool.id}>{content}</div>;
      })}</div></section>
      <div className="notice-card"><span>i</span><p><b>Importante:</b> la carpeta original se conserva intacta. Las salidas se generan en una carpeta separada y las acciones que modifican un workspace crean backup cuando corresponde.</p></div>
    </div>
  );
}

function projectToolAvailable(tool, workspace, standaloneTools = new Set(["extract", "inspect", "archives"])) {
  if (standaloneTools.has(tool.id)) return true;
  if (!workspace.isComplete) return false;
  if (tool.id === "rebuild" || tool.id === "cia") return workspace.hasExeFs && workspace.hasExheader;
  if (tool.id === "patch") return workspace.hasExeFs;
  return true;
}

function ProjectToolsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const focus = searchParams.get("focus") || "extract";
  const selected = projectTools.find((tool) => tool.id === focus) || projectTools[0];
  const { workspace } = useWorkspace();
  const frameRef = useRef(null);
  const requiresWorkspace = !["extract", "archives"].includes(selected.id);
  const visibleTools = workspace.isComplete
    ? projectTools.filter((tool) => !tool.games || tool.games.includes(workspace.gameVersion))
    : projectTools;
  const selectedIsUnavailable = workspace.isComplete
    && (!visibleTools.some((tool) => tool.id === selected.id) || !projectToolAvailable(selected, workspace));

  useEffect(() => {
    const frame = frameRef.current;
    if (!frame) return;
    const visibleSections = {
      extract: ["extract"],
      inspect: ["inspect"],
      build: ["inspect", "build"],
      rebuild: ["inspect", "rebuild-rom"],
      cia: ["inspect", "rebuild-cia"],
      patch: ["inspect", "patch"],
      archives: ["garc", "darc", "sarc", "farc"],
      titlescreen: ["inspect", "titlescreen"],
    }[selected.id] || [selected.section];
    const configure = () => {
      const doc = frame.contentDocument;
      if (!doc) return;
      const styleId = "pk3ds-react-project-style";
      if (!doc.getElementById(styleId)) {
        const style = doc.createElement("style");
        style.id = styleId;
        style.textContent = "body > footer,.editor-intro{display:none!important}main.editor-main{padding-top:1rem!important}main.editor-main > section[hidden]{display:none!important}";
        doc.head.appendChild(style);
      }
      doc.querySelectorAll("main.editor-main > section").forEach((section) => {
        section.hidden = !visibleSections.includes(section.id);
      });
      const workspaceInput = doc.getElementById("workspace");
      if (workspaceInput && workspace.path && workspaceInput.value !== workspace.path) {
        workspaceInput.value = workspace.path;
        workspaceInput.dispatchEvent(new Event("input", { bubbles: true }));
        workspaceInput.dispatchEvent(new Event("change", { bubbles: true }));
      }
      const target = frame.contentDocument?.getElementById(selected.section);
      target?.scrollIntoView({ behavior: "smooth", block: "start" });
    };
    if (frame.contentDocument?.readyState === "complete") configure();
    frame.addEventListener("load", configure);
    const timer = window.setTimeout(configure, 250);
    return () => { frame.removeEventListener("load", configure); window.clearTimeout(timer); };
  }, [selected.id, selected.section, workspace.path]);

  return (
    <div className="page tools-page">
      <div className="page-toolbar"><div><Link className="back-link" to="/project">← Herramientas de proyecto</Link><h1>{selected.label}</h1><p>{selected.id === "inspect" ? "La validación se realiza desde el workspace común de la aplicación." : "Usá el workspace común; esta herramienta conserva los formularios de la versión Windows."}</p></div><span className="status-chip">Vista enfocada</span></div>
      <div className="tools-layout"><ProjectToolRail selected={selected} visibleTools={visibleTools} workspace={workspace} setSearchParams={setSearchParams} />{selectedIsUnavailable ? <UnavailableTool tool={selected} workspace={workspace} /> : selected.id === "inspect" ? <WorkspaceSummary /> : <div className="legacy-host"><LegacyView externalRef={frameRef} src="/legacy/project.html?v=2" title="Herramientas de proyecto" stateKey="project" requireWorkspace={requiresWorkspace} autoLoadButtonId="inspect-action" workspacePanelSelector="#inspect" hideWorkspacePanel={requiresWorkspace} /></div>}</div>
    </div>
  );
}

function ProjectToolRail({ selected, visibleTools, workspace, setSearchParams }) {
  return <nav className="tools-rail" aria-label="Herramientas de proyecto">{visibleTools.map((tool, index) => {
    const available = projectToolAvailable(tool, workspace);
    return <button className={`${tool.id === selected.id ? "is-current" : ""} ${!available ? "is-disabled" : ""}`} type="button" key={tool.id} onClick={() => available && setSearchParams({ focus: tool.id })} disabled={!available}><span>{index + 1}</span><b>{tool.label}</b><small>{available ? tool.eyebrow : "Faltan archivos"}</small></button>;
  })}</nav>;
}

function UnavailableTool({ tool, workspace }) {
  const reason = tool.games && workspace.isComplete && !tool.games.includes(workspace.gameVersion)
    ? `Esta herramienta solo corresponde a ${tool.games.join(" y ")}.`
    : tool.id === "patch"
      ? "Esta herramienta requiere un ExeFS con code.bin descomprimido."
      : "Esta herramienta requiere ExeFS y exheader.bin en el workspace.";
  return <section className="workspace-gate"><span>—</span><h2>{tool.label} no disponible</h2><p>{reason}</p></section>;
}

function WorkspaceSummary() {
  const { workspace } = useWorkspace();
  const modules = workspace.modules || [];
  return <section className="workspace-summary"><div className="card-heading"><div><p className="kicker">VALIDACIÓN CENTRAL</p><h2>{workspace.isComplete ? `${workspace.gameVersion} disponible` : "Workspace pendiente"}</h2></div><span className={`status-chip ${workspace.isComplete ? "is-ready" : ""}`}>{workspace.isComplete ? "RomFS válida" : "Requiere carga"}</span></div><p className="muted">La RomFS se detectó correctamente. ExeFS: <b>{workspace.hasExeFs ? "disponible" : "no detectado"}</b>; exheader: <b>{workspace.hasExheader ? "disponible" : "no detectado"}</b>. Las funciones se habilitan según esos componentes.</p>{workspace.isComplete ? <div className="module-grid">{modules.map((module) => <div className={`module-status ${module.sourceAvailable ? "is-ready" : "is-disabled"}`} key={module.id}><b>{module.name}</b><small>{module.sourceAvailable ? "Disponible" : module.requirement}</small></div>)}</div> : <div className="workspace-gate-inline"><span>↑</span><p>Usá el selector común de arriba y presioná <b>Cargar</b> para validar el workspace.</p></div>}</section>;
}

function RandomizerPage() {
  return <div className="page randomizer-page"><div className="page-intro"><p className="eyebrow">RANDOMIZADOR</p><h1>Personalizá tu juego</h1><p>Las opciones se habilitan después de validar el workspace común. El flujo y los nombres conservan la lógica de pk3DS para Windows.</p></div><LegacyView src="/legacy/index.html?v=2" title="Randomizador" stateKey="randomizer" requireWorkspace autoLoadButtonId="inspect" workspacePanelSelector="#game" hideWorkspacePanel /></div>;
}

function CategoryPage() {
  const { group } = useParams();
  const { workspace, moduleAvailable, moduleInfo } = useWorkspace();
  const allPages = editorPages.filter((page) => page.group === group);
  const pages = workspace.isComplete ? allPages.filter((page) => moduleAvailable(page.availabilityId || page.id)) : allPages;
  const title = group === "CRO" ? "CRO y salida" : group;
  return <div className="page"><div className="category-title"><h1>{title}</h1></div><div className="editor-grid">{pages.map((page) => {
    const moduleId = page.availabilityId || page.id;
    const available = moduleAvailable(moduleId);
    const info = moduleInfo(moduleId);
    const content = <><span className="editor-mark">{group === "RomFS" ? "◈" : group === "ExeFS" ? "◉" : "◆"}</span><div><h2>{page.label}</h2><p>{page.description}</p>{!available && <small className="disabled-note">{info?.requirement || "Cargá y validá un workspace compatible."}</small>}</div><span className="arrow">→</span></>;
    return available
      ? <Link className="editor-card" to={`/editor/${page.id}`} key={page.id}>{content}</Link>
      : <div className="editor-card is-disabled" aria-disabled="true" key={page.id}>{content}</div>;
  })}</div></div>;
}

function EditorPage() {
  const { id } = useParams();
  const page = editorPages.find((item) => item.id === id);
  if (!page) return <NotFound />;
  const groupLabel = page.group === "CRO" ? "CRO / Salida" : page.group;
  return <div className="page editor-page"><div className="page-toolbar"><div><Link className="back-link" to={`/category/${page.group}`}>← {groupLabel}</Link><h1>{page.label}</h1><p>{page.description} La sesión se conserva al navegar a otro módulo.</p></div><span className="status-chip">Editor</span></div><LegacyView src={`/legacy/${page.legacy}?v=3`} title={page.label} stateKey={page.id} requireWorkspace moduleId={page.availabilityId || page.id} hideWorkspacePanel /></div>;
}

function LegacyView({ src, title, stateKey = src, externalRef, requireWorkspace = false, moduleId, hideWorkspacePanel = false, workspacePanelSelector = ".editor-main > section:has(.path-input)", autoLoadButtonId = "load" }) {
  const localFrameRef = useRef(null);
  const frameRef = externalRef || localFrameRef;
  const loadedFrameRef = useRef(null);
  const [loaded, setLoaded] = useState(false);
  const { workspace, moduleAvailable, moduleInfo } = useWorkspace();
  const storageKey = `${FRAME_STATE_PREFIX}${stateKey}`;
  const canOpen = !requireWorkspace || moduleAvailable(moduleId);
  const availability = moduleInfo(moduleId);

  const restore = useCallback(() => {
    const doc = frameRef.current?.contentDocument;
    if (!doc) return;
    const styleId = "pk3ds-react-embed-style";
    if (!doc.getElementById(styleId)) {
      const style = doc.createElement("style");
      style.id = styleId;
      style.textContent = `.site-header{display:none!important}.editor-main,.main,.content-main{padding-top:1.2rem!important}html{scroll-behavior:smooth}body{min-height:100vh}${hideWorkspacePanel ? `${workspacePanelSelector}{display:none!important}` : ""}`;
      doc.head.appendChild(style);
    }
    try {
      const saved = JSON.parse(localStorage.getItem(storageKey) || "{}");
      Object.entries(saved).forEach(([id, value]) => {
        const element = doc.getElementById(id) || doc.querySelector(`[name="${CSS.escape(id)}"]`);
        if (!element || element.type === "file") return;
        if (element.type === "checkbox" || element.type === "radio") element.checked = Boolean(value.checked);
        else element.value = value.value ?? "";
        element.dispatchEvent(new Event("input", { bubbles: true }));
        element.dispatchEvent(new Event("change", { bubbles: true }));
      });
    } catch {
      // A corrupt draft should never prevent opening the editor.
    }
    setLoaded(true);
  }, [frameRef, hideWorkspacePanel, storageKey, workspacePanelSelector]);

  const loadFrame = useCallback(() => {
    const frame = frameRef.current;
    if (!frame || loadedFrameRef.current === frame) return;
    const doc = frame.contentDocument;
    if (!doc) return;
    // A cached iframe may expose a document before its legacy script has
    // finished creating the controls. Do not consume the one-shot guard yet;
    // the load event/fallback will call us again once the button exists.
    if (doc.readyState !== "complete" || (requireWorkspace && !doc.getElementById(autoLoadButtonId))) return;
    loadedFrameRef.current = frame;
    restore();
    const save = () => {
      const values = {};
      doc.querySelectorAll("input[id], select[id], textarea[id], input[name], select[name], textarea[name]").forEach((element) => {
        const id = element.id || element.name;
        if (!id || element.type === "file") return;
        values[id] = element.type === "checkbox" || element.type === "radio" ? { checked: element.checked } : { value: element.value };
      });
      localStorage.setItem(storageKey, JSON.stringify(values));
    };
    doc.addEventListener("input", save, true);
    doc.addEventListener("change", save, true);
    frame.__pk3dsSave = save;
    const workspaceInput = doc.getElementById("workspace");
    const loadButton = doc.getElementById(autoLoadButtonId);
    if (requireWorkspace && workspace.isComplete && workspace.path && loadButton) {
      workspaceInput?.setAttribute("value", workspace.path);
      if (workspaceInput) {
        workspaceInput.value = workspace.path;
        workspaceInput.dispatchEvent(new Event("input", { bubbles: true }));
        workspaceInput.dispatchEvent(new Event("change", { bubbles: true }));
      }
      window.setTimeout(() => loadButton.click(), 0);
    }
  }, [autoLoadButtonId, frameRef, requireWorkspace, restore, storageKey, workspace.isComplete, workspace.path]);

  useEffect(() => {
    if (!canOpen) {
      setLoaded(false);
      loadedFrameRef.current = null;
      return undefined;
    }
    const frame = frameRef.current;
    if (!frame) return undefined;
    loadedFrameRef.current = null;
    frame.addEventListener("load", loadFrame);
    // A cached iframe can finish between the readyState check and listener registration.
    // Give the legacy page one bounded fallback pass so the shared workspace is still loaded.
    const fallbackTimer = window.setTimeout(loadFrame, 350);
    if (frame.contentDocument?.readyState === "complete") loadFrame();
    return () => {
      frame.removeEventListener("load", loadFrame);
      window.clearTimeout(fallbackTimer);
      frame.__pk3dsSave?.();
      frame.__pk3dsSave = null;
    };
  }, [canOpen, frameRef, loadFrame]);

  if (!canOpen) return <div className="workspace-gate"><span>!</span><h2>Workspace requerido</h2><p>{availability?.requirement || "Cargá y validá la carpeta extraída desde el selector común de arriba."}</p><Link className="button primary" to="/">Ir al workspace</Link></div>;
  return <div className="legacy-wrapper"><div className="legacy-toolbar"><span className={`legacy-dot ${loaded ? "is-ready" : ""}`} />{loaded ? "Editor listo" : "Abriendo editor…"}<span className="legacy-note">Workspace centralizado · los campos se guardan localmente</span></div><iframe key={`${src}:${workspace.path}:${workspace.isComplete}`} ref={frameRef} className="legacy-frame" src={src} title={title} onLoad={loadFrame} /></div>;
}

function NotFound() {
  return <div className="empty-state"><span>?</span><h1>Página no encontrada</h1><p>Volvé al inicio para elegir una herramienta.</p><Link className="button primary" to="/">Ir al inicio</Link></div>;
}

createRoot(document.getElementById("root")).render(<App />);
