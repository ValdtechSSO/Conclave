# Arquitectura de repositorio para agentes de programación

## Estructura de carpetas de referencia — versión 2

> **Vigencia en este repositorio:** este documento es la propuesta de diseño
> que originó la migración. Las decisiones implementadas bajo `architecture/`,
> `domain/`, los contratos de módulo y `AGENTS.md` son ahora autoritativas si
> existe cualquier diferencia.

> **Estado:** documento de trabajo  
> **Objetivo principal:** cerrar una estructura de carpetas aplicable a un proyecto real, optimizada para humanos y agentes de programación.  
> **Principio rector:** el modelo razona y propone; el repositorio, las herramientas y la CI localizan, validan, autorizan y generan evidencia.  
> **Resultado esperado:** un árbol de proyecto predecible, navegable, verificable y adoptable de forma progresiva.

---

# 1. Decisión principal

El resultado de esta propuesta no es una nueva variante de Clean Architecture ni una plataforma multiagente. Es una **estructura de repositorio** que combina:

1. una arquitectura modular del producto;
2. vertical slices agrupadas por capacidades funcionales cohesivas;
3. conocimiento arquitectónico y de dominio separado del código;
4. contratos semánticos mantenidos por humanos;
5. índices estructurales generados desde el código;
6. recuperación progresiva de contexto;
7. políticas, validaciones y evidencia deterministas;
8. evaluaciones que permitan comprobar si la arquitectura realmente mejora el trabajo de los agentes.

Nombre descriptivo de la arquitectura:

> **Arquitectura modular por capacidades y vertical slices, con un plano de ingeniería para agentes, navegación determinista del repositorio y entrega basada en evidencia.**

En inglés:

> **Modular Vertical Slice Architecture with an Agent Engineering Plane, Deterministic Repository Navigation, and Evidence-First Delivery.**

Para publicación conviene situarla dentro de **Agentic Software Engineering** y evitar presentar `AOSE` como un acrónimo nuevo, porque ya se utiliza históricamente para *Agent-Oriented Software Engineering* aplicada a sistemas multiagente.

---

# 2. Principios que determinan la estructura

## 2.1 La estructura final es el producto principal

La documentación, las herramientas y los índices existen para sostener una estructura de carpetas que permita responder:

```text
¿Dónde está esta funcionalidad?
¿Qué reglas la gobiernan?
¿Qué código la implementa?
¿Qué datos utiliza?
¿Qué otros componentes dependen de ella?
¿Qué tests la verifican?
¿Qué riesgo tiene cambiarla?
¿Qué validaciones son obligatorias?
¿Qué evidencia demuestra que el cambio está completo?
```

## 2.2 Organización por capacidad de negocio

No organizar globalmente por tipos técnicos:

```text
Controllers/
Services/
Repositories/
Dtos/
Validators/
```

Organizar por módulos funcionales:

```text
Modules/
├── Competitions/
├── Seasons/
├── Teams/
├── Classification/
├── Payments/
└── Identity/
```

Esto reduce el espacio de búsqueda y mantiene próximas las piezas que cambian juntas.

## 2.3 Vertical slice primero

Todo nuevo comportamiento de aplicación nace dentro de la feature funcional que
posee su vocabulario, estado y ciclo de vida:

```text
src/Modules/{Module}/Features/{FeatureArea}/
```

No existe una carpeta global o local `Application/Services` como destino predeterminado.

Regla normativa:

> **New application behavior MUST start inside the owning module and the
> smallest cohesive feature area that owns its vocabulary, state, and lifecycle.
> A command, handler, or use case MUST NOT automatically create a new root
> feature directory.**

Una feature raíz representa una capacidad funcional cohesiva dentro del módulo,
no necesariamente un único comando, handler o caso de uso.

Varias operaciones permanecen en la misma feature cuando:

- actúan sobre el mismo concepto o agregado;
- comparten estado, almacenamiento o ciclo de vida;
- están gobernadas por los mismos invariantes;
- obligan a revisar sustancialmente el mismo contexto;
- separarlas aumentaría la navegación sin establecer un límite real.

Una nueva feature raíz se crea únicamente cuando existe independencia actual y
verificable, por ejemplo:

- vocabulario funcional propio;
- invariantes o perfil de riesgo diferentes;
- ownership independiente;
- dependencias claramente distintas;
- evolución y pruebas mayormente independientes;
- un límite que puede comprobarse mediante reglas de arquitectura.

La estructura interna también se crea bajo demanda. Varias operaciones pequeñas
pueden compartir directamente la carpeta de feature. Una operación obtiene una
subcarpeta propia cuando su implementación tiene suficiente contenido o evolución
independiente. No se crea un nivel de carpetas por cada comando o clase.

Ejemplo:

```text
Modules/Planning/Features/
├── Plan/
│   ├── PlanOrchestrator.cs
│   ├── Prompts/
│   ├── Schemas/
│   └── Validation/
├── Environment/
│   └── DoctorService.cs
└── Run/
    ├── ShowRunService.cs
    └── PruneRunService.cs
```

`ShowRun` y `PruneRun` permanecen bajo `Run` porque operan sobre el mismo
concepto, almacenamiento y ciclo de vida. Agrupaciones genéricas como
`Operations`, `Services` o `Handlers` no sustituyen un nombre funcional.

El código solo se promociona fuera del slice cuando existe una razón mecánica y verificable.

## 2.4 Clean Architecture local

Clean Architecture puede aplicarse dentro de cada módulo, no como una jerarquía global para todo el sistema.

```text
System
└── Bounded Contexts / Modules
    └── Features
        └── Local boundaries
```

Los módulos contienen, cuando sea necesario:

```text
Domain/
Contracts/
Features/
Infrastructure/
```

## 2.5 Economía estructural

La estructura se crea bajo demanda. Un árbol más grande no es más fiel a esta
arquitectura por el mero hecho de reproducir todas las carpetas de referencia.

Regla normativa:

> **Every module, project, directory, abstraction, and shared component MUST be
> justified by current code or by an enforced boundary. Hypothetical future use
> is not sufficient.**

Por tanto:

- no se crean carpetas vacías para completar una plantilla;
- varias clases pequeñas y cohesivas pueden compartir archivo cuando se localizan,
  cambian y se revisan juntas;
- una clase obtiene archivo propio cuando tiene responsabilidad, navegación o
  evolución independiente, no por una regla mecánica;
- un proyecto o assembly existe solo cuando impone un límite verificable de
  dependencias, despliegue, ownership, lenguaje o runtime;
- los módulos representan capacidades funcionales, nunca categorías técnicas
  como `Git`, `Providers`, `Repositories` o `Validation`;
- una capacidad no se divide en varios módulos hasta que las partes tengan
  vocabulario, ownership, contratos o ciclo de vida realmente independientes;
- `BuildingBlocks`, `Shared` y abstracciones comunes aparecen únicamente cuando
  dos o más consumidores actuales justifican la promoción y el ownership queda
  explícito.

Las carpetas mostradas en los árboles canónicos son posibilidades permitidas,
no una lista de placeholders obligatorios. La estructura mínima que explica y
protege el sistema es preferible a una jerarquía exhaustiva sin contenido.

## 2.6 Los contratos manuales no duplican el código

Un contrato manual describe únicamente información no derivable con fiabilidad:

- significado del módulo;
- vocabulario e intenciones;
- ownership como decisión arquitectónica;
- riesgo predeterminado;
- invariantes;
- enlaces a ADRs;
- límites conceptuales.

No debe contener:

- paths;
- endpoints detectables;
- handlers;
- clases;
- entidades utilizadas;
- tablas observadas;
- tests;
- referencias.

Esa información se genera desde el código.

## 2.7 El contexto se recupera progresivamente

El agente comienza con contexto mínimo y solicita más información conforme entiende el problema.

No se le entrega por defecto un paquete grande y cerrado de archivos seleccionado antes de comenzar.

```text
Minimal bootstrap
→ Locate
→ Inspect
→ Expand context
→ Analyze impact
→ Expand or prune
→ Implement
```

## 2.8 Sin evidencia no hay completitud

Una tarea no está completa porque el agente lo afirme.

La completitud se deriva de:

- revisión del estado del repositorio;
- build;
- tests;
- análisis estático;
- validaciones específicas del riesgo;
- evidencia ligada al commit;
- aprobación cuando corresponda.

## 2.9 La propuesta debe ser falsable

La estructura incluye datasets, graders y resultados de evaluación para comparar:

```text
Repositorio convencional
vs.
Contexto estático
vs.
Arquitectura adaptativa basada en índices, herramientas y evidencia
```

---

# 3. Árbol canónico del repositorio

Leyenda:

- **[M]** mantenido manualmente y autoritativo;
- **[G]** generado desde código o configuración;
- **[R]** estado de ejecución o artefacto de CI;
- **[O]** opcional según el tipo de proyecto.

```text
repository/
├── AGENTS.md                                      [M]
├── README.md                                      [M]
├── .gitignore                                     [M]
│
├── architecture/                                  [M]
│   ├── README.md
│   ├── system-overview.md
│   ├── principles.md
│   ├── boundaries.md
│   ├── data-ownership.md
│   ├── quality-attributes.md
│   └── decisions/
│       ├── ADR-001-modular-architecture.md
│       ├── ADR-002-feature-first-slices.md
│       ├── ADR-003-agentic-repository.md
│       └── ...
│
├── domain/                                        [M]
│   ├── README.md
│   ├── glossary.md
│   ├── global-invariants.md
│   └── contexts/
│       ├── competitions.md
│       ├── seasons.md
│       ├── teams.md
│       ├── classification.md
│       ├── payments.md
│       └── identity.md
│
├── src/                                           [M]
│   ├── BuildingBlocks/
│   ├── Modules/
│   │   ├── Competitions/
│   │   │   ├── AGENTS.md
│   │   │   ├── module.contract.yml
│   │   │   ├── Domain/
│   │   │   ├── Contracts/
│   │   │   ├── Features/
│   │   │   └── Infrastructure/
│   │   ├── Classification/
│   │   │   ├── AGENTS.md
│   │   │   ├── module.contract.yml
│   │   │   ├── Domain/
│   │   │   ├── Contracts/
│   │   │   ├── Features/
│   │   │   └── Infrastructure/
│   │   ├── Payments/
│   │   └── Identity/
│   └── Hosts/
│       ├── Api/
│       ├── Workers/                               [O]
│       └── Mcp/                                   [O]
│
├── web/                                           [O]
│   └── src/
│       └── app/
│           ├── shell/
│           ├── core/
│           ├── shared/
│           └── modules/
│               ├── competitions/
│               ├── classification/
│               ├── payments/
│               └── identity/
│
├── tests/                                         [M]
│   ├── Unit/
│   ├── Integration/
│   ├── Contract/
│   ├── Architecture/
│   ├── EndToEnd/
│   ├── Performance/
│   ├── Security/
│   └── Agentic/
│       ├── Navigation/
│       ├── ContextRetrieval/
│       ├── ImpactAnalysis/
│       ├── Policy/
│       └── Evidence/
│
├── docs/                                          [M/G]
│   ├── development/
│   ├── testing/
│   ├── operations/
│   ├── runbooks/
│   └── api/                                       [G]
│
├── tools/                                         [M]
│   ├── Agentic.Cli/
│   ├── CodeIndexer/
│   ├── ContextBroker/
│   ├── ValidationRunner/
│   ├── EvidenceCollector/
│   └── scripts/
│
└── .agentic/                                      [M/G/R]
    ├── README.md                                  [M]
    ├── contracts/                                 [M]
    │   ├── repository.contract.yml
    │   └── schemas/
    │       ├── module-contract.schema.json
    │       ├── evidence.schema.json
    │       └── task-state.schema.json
    ├── agents/                                    [M]
    ├── workflows/                                 [M]
    ├── skills/                                    [M]
    ├── prompts/                                   [M]
    ├── policies/                                  [M]
    │   ├── risk-levels.yml
    │   ├── permissions.yml
    │   ├── quality-gates.yml
    │   └── state-transitions.yml
    ├── memory/                                    [M]
    │   ├── lessons/
    │   ├── failures/
    │   ├── patterns/
    │   └── deprecated/
    ├── templates/                                 [M]
    │   ├── task-worksheet.md
    │   ├── handoff.md
    │   └── evidence.json
    ├── evals/                                     [M/R]
    │   ├── datasets/
    │   ├── conditions/
    │   ├── graders/
    │   ├── baselines/
    │   └── results/
    ├── generated/                                 [G]
    │   └── index/
    │       ├── repository.json
    │       ├── projects.json
    │       ├── modules.json
    │       ├── symbols.json
    │       ├── references.json
    │       ├── dependencies.json
    │       ├── endpoints.json
    │       ├── handlers.json
    │       ├── entities.json
    │       ├── data-access.json
    │       ├── tests.json
    │       └── documents.json
    └── runtime/                                   [R]
        ├── tasks/
        ├── evidence/
        ├── traces/
        └── cache/
```

---

# 4. Estructura interna de un módulo backend

Estructura canónica:

```text
src/Modules/Classification/
├── AGENTS.md
├── module.contract.yml
├── Domain/
│   ├── Models/
│   ├── Rules/
│   ├── Events/
│   ├── ValueObjects/
│   └── Services/                                  [excepcional]
├── Contracts/
│   ├── Commands/
│   ├── Queries/
│   ├── Events/
│   └── PublicApi/
├── Features/
│   ├── Standings/
│   │   ├── GetClassificationHandler.cs
│   │   ├── RecalculateClassificationHandler.cs
│   │   ├── Validator.cs
│   │   └── Mapping.cs
│   ├── Disqualification/
│   │   ├── DisqualifyTeamHandler.cs
│   │   └── Validator.cs
│   └── Shared/                                    [excepcional]
└── Infrastructure/
    ├── Persistence/
    ├── Queries/
    ├── Messaging/
    ├── ExternalServices/
    └── DependencyInjection.cs
```

## 4.1 Regla de ubicación de la lógica

### Permanece en `Features/{FeatureArea}` cuando

- pertenece a una capacidad funcional cohesiva del módulo;
- orquesta una operación concreta;
- transforma request y response;
- coordina repositorios o servicios para esa capacidad;
- contiene validaciones específicas de la operación.

### Se promociona a `Domain/` cuando

- representa una regla de negocio con significado propio;
- no depende de infraestructura;
- la usan dos o más slices;
- debe permanecer consistente entre varios casos de uso.

### Se promociona a `Contracts/` cuando

- otros módulos necesitan consumirla;
- forma parte de la API pública del módulo;
- es un comando, evento, query o DTO público.

### Se ubica en `Infrastructure/` cuando

- accede a persistencia;
- llama a servicios externos;
- publica o consume mensajes;
- usa SDKs;
- accede al filesystem;
- implementa interfaces técnicas.

### `Features/Shared/` solo se permite cuando

- dos o más slices comparten la misma lógica de aplicación;
- esa lógica no es una regla de dominio;
- existe un test o regla que impide convertirlo en un cajón de sastre.

## 4.2 Carpetas prohibidas por defecto

```text
Application/Services/
Managers/
Helpers/
Common/Business/
Utils/
```

Pueden existir únicamente con una decisión explícita y una responsabilidad acotada.

---

# 5. Estructura interna del frontend

El frontend sigue la misma regla: módulo funcional y feature-first.

```text
web/src/app/modules/classification/
├── AGENTS.md
├── features/
│   ├── view-classification/
│   │   ├── classification-page.component.ts
│   │   ├── classification-page.component.html
│   │   ├── classification-page.component.spec.ts
│   │   ├── classification.store.ts
│   │   ├── classification.api.ts
│   │   ├── classification.models.ts
│   │   └── classification.routes.ts
│   └── configure-tie-breaks/
├── domain/                                        [compartido por 2+ features]
├── data-access/                                   [compartido por 2+ features]
├── ui/                                            [compartido por 2+ features]
└── routes.ts
```

Regla:

> La implementación comienza dentro de la feature. Solo se mueve a `domain`, `data-access` o `ui` cuando dos o más features necesitan compartirla.

`core/` contiene capacidades globales de aplicación, como autenticación, configuración o interceptores.

`shared/` contiene primitives visuales o técnicas realmente independientes del dominio. No debe contener lógica de negocio.

---

# 6. `AGENTS.md`: router global y routers locales

## 6.1 `AGENTS.md` de la raíz

Debe ser breve y contener:

- propósito del repositorio;
- comandos autoritativos;
- reglas críticas;
- mapa de alto nivel;
- workflow inicial;
- herramientas disponibles;
- operaciones prohibidas.

No debe contener toda la arquitectura ni todo el dominio.

## 6.2 `AGENTS.md` del módulo

Debe contener únicamente orientación local:

```markdown
# Classification module

## Purpose

Calculates competition standings and tie-break rules.

## Read before changing

- `module.contract.yml`
- `/domain/contexts/classification.md`
- relevant ADRs returned by `agentic decisions find classification`

## Commands

- `agentic tests find --module classification`
- `agentic validate targeted --module classification`

## Critical rules

- Teams without games must remain visible.
- Disqualified teams are sorted last unless a configured rule overrides it.
- Performance validation is required for query changes.
```

Los hechos estructurales, como endpoints o paths, no se copian aquí si pueden consultarse mediante el índice.

---

# 7. Contratos semánticos del módulo

Archivo:

```text
src/Modules/{Module}/module.contract.yml
```

Ejemplo correcto:

```yaml
id: classification
name: Classification

purpose: >
  Calculates competition standings, rankings and tie-break rules.

intent:
  aliases:
    - classification
    - standings
    - ranking
    - league table
    - clasificación
    - tabla de posiciones
    - desempate

ownership:
  domain: competition-classification
  authoritative_data:
    - ClassificationSnapshots

risk:
  default: high
  reasons:
    - Business-critical calculation
    - Performance-sensitive queries

invariants:
  - domain/contexts/classification.md#team-inclusion
  - domain/contexts/classification.md#teams-without-games
  - domain/contexts/classification.md#disqualified-teams

architecture_decisions:
  - architecture/decisions/ADR-014-classification-rules.md
```

## 7.1 Campos prohibidos

El schema debe rechazar:

```yaml
paths:
entrypoints:
handlers:
classes:
tests:
entities_read:
entities_written:
routes:
```

Esos datos pertenecen al índice generado.

## 7.2 Verificación del contrato

CI contrasta el contrato semántico con la arquitectura observada:

```text
Declared ownership
        ↓
Generated data-access graph
        ↓
Architecture policy
        ↓
Pass / Fail
```

Ejemplos:

- si `Classification` declara propiedad sobre `ClassificationSnapshots`, otro módulo no puede escribir directamente esa tabla;
- todos los enlaces a invariantes y ADRs deben existir;
- el identificador del módulo debe coincidir con el módulo detectado;
- los aliases no deben colisionar de forma no resuelta con otros módulos;
- el índice debe corresponder al commit actual.

---

# 8. Índice estructural generado

Ubicación:

```text
.agentic/generated/index/
```

Es generado por `CodeIndexer` y nunca se edita manualmente.

## 8.1 Información .NET

Se extrae con Roslyn, OpenAPI, configuración EF y build metadata:

- proyectos;
- namespaces;
- símbolos;
- interfaces e implementaciones;
- referencias;
- endpoints;
- handlers;
- registros de DI;
- entidades;
- configuraciones EF;
- tablas;
- eventos;
- producers y consumers;
- dependencias entre módulos;
- tests relacionados.

## 8.2 Información Angular

Se extrae mediante AST y configuración del framework:

- routes;
- components;
- services;
- stores y signals;
- guards;
- interceptors;
- API clients;
- imports;
- dependencias entre features;
- tests.

## 8.3 Grafo de datos

El índice debe permitir recorrer:

```text
Entity
→ EF Configuration
→ Table
→ Migration
→ Repository or Query
→ Feature
→ Endpoint
→ Test
```

## 8.4 Frescura

Cada índice debe contener:

```json
{
  "repositoryRevision": "73a9c45",
  "generatorVersion": "1.0.0",
  "generatedAt": "2026-07-27T10:00:00Z"
}
```

Si el SHA no coincide con el código actual, las herramientas deben regenerarlo o declararlo obsoleto.

---

# 9. Herramientas de navegación

La estructura se explota mediante una CLI reutilizada por humanos, CI y MCP.

```bash
agentic locate "where is classification calculated?"
agentic symbol ClassificationCalculator
agentic references ClassificationCalculator
agentic tests find ClassificationCalculator
agentic impact src/Modules/Classification/Domain/ClassificationCalculator.cs
agentic data owner CompetitorsByPhases
agentic decisions find "classification ordering"
```

Ejemplo de salida:

```text
Intent: Calculate competition classification
Confidence: High

Semantic contract:
  src/Modules/Classification/module.contract.yml

Observed module root:
  src/Modules/Classification

Primary entry points:
  GET /api/competitions/{competitionId}/classification

Core symbols:
  GetClassificationHandler
  ClassificationCalculator

Data access:
  ClassificationRepository

Domain rules:
  domain/contexts/classification.md

Relevant tests:
  tests/Integration/Classification/GetClassificationTests.cs
  tests/Performance/Classification/ClassificationQueryBenchmarks.cs

Risk:
  High
```

La salida diferencia siempre:

- lo declarado semánticamente;
- lo observado en el código;
- lo inferido por búsqueda.

---

# 10. Context Broker progresivo

La carpeta y tooling no deben preseleccionar un paquete cerrado de código al inicio.

## 10.1 Contexto inicial

El agente recibe:

```text
Task
Root AGENTS.md
Repository revision
Risk and permission policies
Available navigation tools
```

## 10.2 Recuperación iterativa

```bash
agentic locate "teams without games missing from classification"
agentic context suggest --intent "teams without games missing from classification"
agentic context expand --module classification --include invariants,entrypoints,tests
agentic impact src/Modules/Classification/Domain/ClassificationCalculator.cs
agentic context expand --symbol ClassificationCalculator --include callers,data,decisions
agentic context prune --task AG-142
```

## 10.3 Estado de recuperación

Se registra en:

```text
.agentic/runtime/tasks/{task-id}/
├── task.json
├── worksheet.md
├── context-log.jsonl
├── decisions.md
└── handoff.md
```

`context-log.jsonl` registra:

- qué solicitó el agente;
- qué resultados recibió;
- procedencia;
- revisión del índice;
- cantidad de contexto;
- motivo de expansión o descarte.

No almacena chain-of-thought privada.

---

# 11. Control and Evidence Plane

Este plano es el núcleo operativo de la estructura.

## 11.1 Carpetas

```text
.agentic/policies/
├── risk-levels.yml
├── permissions.yml
├── quality-gates.yml
└── state-transitions.yml

.agentic/runtime/evidence/{task-id}/{commit-sha}/
├── manifest.json
├── build.json
├── tests.json
├── security.json
├── performance.json
├── review.json
└── unresolved-risks.json
```

## 11.2 Máquina de estados

```text
Created
  ↓
Scoped
  ↓
Implemented
  ↓
TargetValidated
  ↓
FullyValidated
  ↓
EvidenceComplete
  ↓
Reviewed
  ↓
MergeReady
```

El agente propone una transición. El policy engine la acepta o rechaza.

```text
Agent proposes transition
        ↓
Policy engine checks requirements
        ↓
Evidence collector validates artifacts
        ↓
Transition accepted or rejected
```

## 11.3 Evidencia ligada al commit

```json
{
  "taskId": "AG-142",
  "revision": "73a9c45",
  "check": "full-tests",
  "tool": "dotnet",
  "toolVersion": "10.0.1",
  "command": "dotnet test --no-build",
  "exitCode": 0,
  "passed": 342,
  "failed": 0,
  "skipped": 2,
  "artifact": "ci://runs/9182/tests"
}
```

Si cambia el commit, la evidencia anterior no puede satisfacer el gate del nuevo commit.

## 11.4 Evidencia negativa

También se registra:

- checks no ejecutados;
- motivo;
- resultados inconclusos;
- tests flaky;
- limitaciones del entorno;
- riesgos no resueltos.

## 11.5 Clases de evidencia

| Clase | Ejemplos |
|---|---|
| Determinista | Build, test runner, schema validator |
| Observacional | Benchmark, trace, métrica de producción |
| Estática | Linter, SAST, dependency scan |
| Probabilística | Revisión de otro agente o LLM |
| Declarativa | Afirmación del agente |

Una afirmación declarativa nunca satisface por sí sola un quality gate.

---

# 12. Tests del producto y de la arquitectura para agentes

## 12.1 Tests del producto

```text
tests/
├── Unit/{Module}/{FeatureArea}/
├── Integration/{Module}/{FeatureArea}/
├── Contract/{Module}/
├── Architecture/
├── EndToEnd/
├── Performance/{Module}/
└── Security/{Module}/
```

Los paths de tests reflejan los paths funcionales del producto.

Ejemplo:

```text
src/Modules/Classification/Features/Standings/
tests/Unit/Classification/Standings/
tests/Integration/Classification/Standings/
tests/Performance/Classification/Standings/
```

## 12.2 Tests de la infraestructura agentic

```text
tests/Agentic/
├── Navigation/
├── ContextRetrieval/
├── ImpactAnalysis/
├── Policy/
└── Evidence/
```

Ejemplos:

```text
Given: "Where is classification calculated?"
Expected:
  module = Classification
  core symbol includes ClassificationCalculator
  domain document includes classification.md
```

```text
Given: ClassificationCalculator.cs changed
Expected:
  classification tests selected
  performance benchmark selected
  risk = high
```

```text
Given: agent requests production deployment
Expected:
  denied
  human approval required
```

---

# 13. Evaluación falsable

La estructura reserva:

```text
.agentic/evals/
├── datasets/
│   ├── laliguilla.yml
│   ├── vyntrio.yml
│   └── vitara-fitness.yml
├── conditions/
│   ├── baseline.yml
│   ├── static-context.yml
│   └── adaptive-repository.yml
├── graders/
├── baselines/
└── results/
```

## 13.1 Condiciones

### Baseline

```text
Repositorio convencional
+ agente
+ herramientas estándar
```

### Static Context

```text
Repositorio estructurado
+ AGENTS.md
+ paquete inicial de contexto
```

### Adaptive Repository

```text
Repositorio estructurado
+ contratos semánticos
+ índice generado
+ Context Broker iterativo
+ Evidence Gates
```

## 13.2 Métricas

Primarias:

- acceptance tests superados;
- merge blockers;
- defectos introducidos;
- falsas declaraciones de completitud;
- violaciones de scope.

Navegación:

- tiempo hasta el primer archivo relevante;
- precision y recall de localización;
- recall de selección de tests;
- wrong-module rate;
- precisión del análisis de impacto.

Eficiencia:

- tokens;
- tool calls;
- coste;
- duración;
- contexto relevante frente a contexto total.

La estructura se considera útil solo si mejora resultados medibles frente al baseline.

---

# 14. Matriz de fuentes de verdad

| Información | Fuente de verdad | Mantenimiento |
|---|---|---|
| Propósito del módulo | `module.contract.yml` | Manual |
| Aliases funcionales | `module.contract.yml` | Manual |
| Ownership deseado | `module.contract.yml` + ADR | Manual |
| Invariantes | `domain/` + tests | Manual y ejecutable |
| Paths reales | índice generado | Automático |
| Endpoints | código/OpenAPI/índice | Automático |
| Handlers y símbolos | índice generado | Automático |
| Datos leídos y escritos | análisis generado | Automático |
| Tests relacionados | índice generado | Automático |
| Dependencias | índice generado + architecture tests | Automático |
| Riesgo | policy + contrato semántico | Manual |
| Validaciones requeridas | `quality-gates.yml` | Manual |
| Resultado de validaciones | evidence manifest | Automático |
| Estado de tarea | policy engine | Automático |
| Lecciones y fallos | `.agentic/memory/` | Curado |

---

# 15. Reglas de CI sobre la estructura

CI debe validar:

1. todos los `module.contract.yml` cumplen el schema;
2. no contienen campos estructurales prohibidos;
3. los enlaces a invariantes y ADRs existen;
4. el índice fue generado para el commit actual;
5. ownership declarado coincide con las políticas de acceso observadas;
6. no existen dependencias prohibidas entre módulos;
7. nuevos handlers están dentro de `Features/{FeatureArea}` y no directamente en
   `Features/`; una feature raíz nueva requiere una capacidad funcional justificable;
8. endpoints no acceden directamente a Infrastructure;
9. `BuildingBlocks` no contiene lógica de negocio;
10. `shared`, `common`, `helpers` y `services` no crecen sin una excepción aprobada;
11. los tests requeridos por el impacto detectado fueron ejecutados;
12. el estado `MergeReady` solo se alcanza con evidencia completa.

---

# 16. Contenido de `.agentic`

## `agents/`

Configuraciones o responsabilidades de agentes especializados opcionales. No obliga a usar multiagente.

## `workflows/`

Ciclos completos:

```text
feature.md
bug-fix.md
database-migration.md
performance-change.md
security-change.md
incident.md
```

## `skills/`

Procedimientos especializados y reutilizables:

```text
create-backend-feature/
create-angular-feature/
database-migration/
query-optimization/
review-pull-request/
```

## `prompts/`

Prompts versionados con schema, ejemplos y tests. Solo se almacenan aquí los prompts del proceso de ingeniería. Los prompts que forman parte del producto viven dentro del módulo del producto.

## `policies/`

Control determinista de riesgo, permisos, quality gates y estados.

## `memory/`

Conocimiento operacional curado:

```text
lessons/
failures/
patterns/
deprecated/
```

No almacena conversaciones completas.

## `templates/`

Plantillas de worksheet, handoff y evidencia.

## `evals/`

Protocolo experimental y resultados.

## `generated/`

Todo lo derivado automáticamente. Puede regenerarse y nunca es fuente semántica primaria.

## `runtime/`

Estado de tareas, evidencia, trazas y caché. Parte de este contenido puede mantenerse fuera de Git y almacenarse como artefacto de CI.

---

# 17. Política de versionado y Git

## Se versiona

- arquitectura;
- dominio;
- contratos semánticos;
- workflows;
- skills;
- prompts;
- políticas;
- templates;
- datasets y graders;
- tooling;
- schemas.

## Puede versionarse o regenerarse

- índices generados, según coste de generación y necesidades de revisión.

Recomendación inicial: regenerarlos en CI y no tratarlos como fuente de verdad manual.

## No se versiona normalmente

```text
.agentic/runtime/cache/
.agentic/runtime/traces/
.agentic/runtime/evidence/
.agentic/evals/results/
```

Pueden almacenarse como artefactos de CI o en un sistema de observabilidad.

Los worksheets y handoffs pueden versionarse cuando representen decisiones o continuidad importante; no es obligatorio conservar cada ejecución trivial.

---

# 18. Estructura mínima para comenzar

No es necesario crear todo el árbol el primer día.

MVP:

```text
repository/
├── AGENTS.md
├── architecture/
│   ├── system-overview.md
│   └── decisions/
├── domain/
│   ├── glossary.md
│   ├── global-invariants.md
│   └── contexts/
├── src/
│   └── Modules/
│       └── {Module}/
│           ├── AGENTS.md
│           ├── module.contract.yml
│           ├── Domain/
│           ├── Contracts/
│           ├── Features/
│           └── Infrastructure/
├── tests/
│   ├── Unit/
│   ├── Integration/
│   └── Architecture/
├── tools/
│   └── Agentic.Cli/
└── .agentic/
    ├── workflows/
    ├── skills/
    ├── policies/
    ├── templates/
    ├── generated/index/
    └── runtime/
```

Este árbol se materializa solo hasta el nivel necesario. Por ejemplo, un módulo
sin reglas de dominio propias no crea `Domain/`, y un repositorio con un único
módulo no crea `BuildingBlocks/` ni `Shared/` preventivamente.

---

# 19. Implementación progresiva

## Fase 1 — Estructura y contratos

Crear:

- árbol mínimo;
- `AGENTS.md` raíz;
- módulos funcionales;
- `module.contract.yml` semánticos;
- arquitectura y dominio esenciales.

## Fase 2 — Reglas de ubicación

Aplicar a código nuevo:

```text
src/Modules/{Module}/Features/{FeatureArea}/
```

No migrar masivamente todo el código existente. Reorganizar al tocar cada área.

## Fase 3 — Índice generado

Generar símbolos, dependencias, endpoints, acceso a datos y tests.

## Fase 4 — Navegación

Implementar:

```bash
agentic locate
agentic symbol
agentic references
agentic tests find
agentic impact
```

## Fase 5 — Context Broker

Implementar recuperación iterativa y registro de procedencia.

## Fase 6 — Control y evidencia

Implementar máquina de estados, quality gates y manifests ligados al commit.

## Fase 7 — Evals

Ejecutar el experimento baseline/static/adaptive.

## Fase 8 — MCP

Exponer los mismos servicios de CLI mediante MCP.

## Fase 9 — Búsqueda semántica

Añadirla solo si los índices y búsqueda lexical no alcanzan el recall necesario.

## Fase 10 — Multiagente

Añadir especialistas únicamente cuando los experimentos demuestren una mejora.

---

# 20. Flujo resultante

```text
Human Task
    ↓
Minimal Bootstrap Context
    ↓
AGENTS.md Router
    ↓
Progressive Context Broker
    ├── Semantic module contracts
    ├── Generated code index
    ├── Symbol and lexical search
    ├── Dependency and data analysis
    └── Optional semantic fallback
    ↓
Feature-First Product Architecture
    ├── Domain
    ├── Contracts
    ├── Features
    └── Infrastructure
    ↓
Agent Implementation
    ↓
Deterministic Control Plane
    ├── Risk policy
    ├── Permission gates
    ├── State machine
    └── Validation runner
    ↓
Evidence Plane
    ├── Build
    ├── Tests
    ├── Security
    ├── Performance
    ├── Review
    └── Unresolved risks
    ↓
Human or policy approval
    ↓
Merge readiness
```

---

# 21. Decisiones cerradas en esta versión

1. El outcome principal es el árbol de carpetas del proyecto.
2. El código se organiza por módulos funcionales y vertical slices.
3. No existe `Application/Services` como ubicación predeterminada.
4. La lógica nueva nace dentro de la feature.
5. Una feature raíz representa una capacidad cohesiva, no automáticamente un
   comando, handler o caso de uso; las operaciones que comparten concepto y ciclo
   de vida permanecen juntas.
6. La estructura interna de una feature se materializa solo cuando la complejidad
   o evolución independiente la justifica.
7. Los contratos manuales contienen solo semántica no derivable.
8. Paths, endpoints, símbolos, acceso a datos y tests se generan.
9. CI contrasta ownership declarado con arquitectura observada.
10. `AGENTS.md` actúa como router, no como enciclopedia.
11. El contexto se recupera iterativamente mediante un Context Broker.
12. El contexto inicial es mínimo.
13. El plano de control y evidencia es una parte de primer nivel de la estructura.
14. La evidencia queda ligada al commit.
15. Una declaración del agente no satisface un quality gate.
16. La arquitectura contiene evaluaciones falsables.
17. La búsqueda semántica es un fallback.
18. Multiagente es opcional y debe justificarse con métricas.
19. Para publicación se evita reclamar `AOSE` como un nuevo acrónimo.

---

# 22. Criterio final de éxito

La estructura habrá cumplido su objetivo cuando un agente nuevo pueda:

1. recibir una tarea con un contexto inicial mínimo;
2. localizar el módulo y la feature correctos;
3. recuperar las reglas e invariantes relevantes;
4. identificar código, datos, dependencias y tests afectados;
5. implementar el cambio dentro de una ubicación predecible;
6. ejecutar las validaciones exigidas por el riesgo;
7. generar evidencia ligada al commit;
8. detenerse correctamente si falta aprobación o evidencia;
9. entregar el trabajo a otro agente o humano sin reconstruir la sesión completa.

La estructura de carpetas no es una convención estética. Es el mecanismo que hace posible una navegación, ejecución y validación confiables para humanos y agentes.
