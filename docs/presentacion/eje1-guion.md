# Guion de presentación · Eje 1 — Arquitectura de la solución

Acompaña a [`eje1-arquitectura.html`](eje1-arquitectura.html). Abrirlo en el navegador (funciona sin internet) y avanzar con **→** / **←**.
Duración sugerida: **17–22 minutos** + preguntas. Cada sección trae: la idea que debe quedar, qué señalar, qué decir y preguntas probables.

> Regla para toda la presentación: **cada afirmación importante tiene un dato medido detrás**. Si preguntan "¿cómo sabes?", la respuesta siempre es "lo probé así" (ver `docs/pruebas.md`).

---

## 0 · Inicio (1,5 min)

**Idea que debe quedar:** es un sistema que no pierde eventos, y eso está demostrado, no prometido.

**Señalar:** la fila de 6 indicadores, de izquierda a derecha.

**Qué decir:**
- "El reto es que en temporada pico el volumen se multiplica por cuatro y no podemos perder ni un evento ni degradar la operación. Diseñé para 5.000 eventos por segundo sostenidos."
- "Lo más importante de esta pantalla es el **cero**: aceptamos 138.375 eventos en pruebas de carga y los 138.375 llegaron a la base. Una de esas corridas la hice **matando el procesador a la mitad**."
- "Las cifras de volumen son supuestos, no datos reales de TCC: los declaro así para que las decisiones sean discutibles."
- Mencionar los SLO de la tabla derecha: "estos son el contrato con el negocio; todo lo demás está al servicio de cumplirlos."

**Preguntas probables:**
- *¿Por qué 5.000 ev/s?* → 2,4 M guías × 8 eventos = 19,2 M/día; concentrado en horas pico da ~1.350 ev/s con ráfagas de 5.000. Diseño para la ráfaga y pruebo al doble.
- *¿Por qué 24 particiones?* → ~500 ev/s por partición con margen ×2; además es el techo de instancias paralelas del procesador, así que es una decisión de escalabilidad.

---

## 1 · Componentes (3 min)

**Idea que debe quedar:** Kafka en el centro desacopla a todos; cada pieza falla sola.

**Señalar el diagrama de izquierda a derecha:**
1. **Emisores → API**: "TMS, transporte, internacional y automatización hablan con una sola API; cada uno se autentica con su propio token de Keycloak."
2. **API**: "Valida, autoriza y publica en Kafka. **No toca la base de datos en el camino normal**: por eso escala horizontalmente sin límite."
3. **Redis y contingencia (líneas punteadas)**: "Redis es solo una optimización para descartar duplicados. La contingencia en PostgreSQL entra **solo si Kafka no responde**, y un relay reenvía cuando vuelve."
4. **Kafka `guias.eventos.recibidos`**: "24 particiones, la **clave es el número de guía**: todos los eventos de una guía van a la misma partición y conservan el orden sin bloqueos."
5. **Procesador**: "Es la única fuente de verdad del estado. En una transacción guarda historial, estado y el mensaje de salida."
6. **Outbox → `guias.estados.cambiados` → Notificador**: "Solo se publican cambios confirmados. De ahí consume el notificador y, en el futuro, las integraciones y el agente de IA (recuadro punteado): **agregar un consumidor no toca a nadie**."

**Qué decir para cerrar:** "Si el proveedor de SMS cae, solo crece la cola del notificador; la ingesta y el rastreo siguen."

**Preguntas probables:**
- *¿Por qué la API no guarda primero en la base?* → Sería un cuello de botella en el camino crítico. Kafka con `acks=all` y 3 réplicas ya es durable; la base solo entra como contingencia.
- *¿Qué es un "servicio .NET" en el diagrama?* → Tres procesos independientes (API, procesador, notificador), cada uno escalable por separado, sobre una arquitectura hexagonal de 7 proyectos.

---

## 2 · Arquitectura hexagonal (2 min)

**Idea que debe quedar:** el negocio no conoce la tecnología; la tecnología se enchufa en los bordes, y esa regla la verifica el pipeline.

**Señalar el diagrama de izquierda a derecha:**
1. **Hosts (izquierda):** "Los tres servicios son solo **adaptadores de entrada**: traducen HTTP o un mensaje de Kafka en una llamada a un caso de uso. Casi no tienen lógica; arman la inyección de dependencias."
2. **Aplicación (centro, azul):** "Aquí están los cuatro casos de uso: recibir, procesar, notificar y reenviar la contingencia. Solo hablan con **interfaces**."
3. **Dominio (verde, en el centro de todo):** "La máquina de estados y las reglas: qué evento se aplica, cuál es tardío o inválido y cuándo se notifica. **No depende de nada**, ni siquiera de una librería."
4. **Puertos (naranja, en el borde):** "Siete interfaces. Cada una existe porque un caso de uso la necesita, no por plantilla."
5. **Adaptadores (derecha):** "Kafka, PostgreSQL y Redis son detalles que implementan esos puertos."
6. **Decoradores (amarillo):** "La resiliencia también es un adaptador. La contingencia y el filtro tolerante **envuelven** un puerto con otro puerto. `RecibirEvento` no sabe que existe una contingencia: eso es abierto/cerrado de SOLID."

**Tarjetas de abajo:**
- "Las dependencias apuntan siempre hacia adentro, y hay **8 pruebas de arquitectura**. Si alguien importa Kafka en el dominio, el pipeline falla; no depende de la disciplina de cada desarrollador."
- "El beneficio más concreto: 51 pruebas de dominio y aplicación corren en milisegundos con falsos escritos a mano, sin contenedores."
- "Y es la base de la portabilidad a Java: los mismos módulos en Maven y ArchUnit para las mismas reglas."

**Preguntas probables:**
- *¿Por qué el GET no pasa por un caso de uso?* → Es el lado de consulta (CQRS): una lectura sin reglas de negocio no justifica un caso de uso. Tiene su propia interfaz (`IConsultaGuias`) para poder cambiarla por Redis sin tocar el endpoint.
- *¿No es sobreingeniería?* → Hay 7 puertos, uno por dependencia real, y no uso MediatR ni repositorios genéricos. El costo es de unos pocos archivos; a cambio, la resiliencia se agregó sin tocar la lógica y los casos de uso se prueban sin infraestructura.
- *¿Dónde están SOLID y los patrones?* → Inversión de dependencias (los puertos), abierto/cerrado (decoradores), responsabilidad única (un caso de uso por clase). Patrones: puertos y adaptadores, decorador, circuit breaker, outbox/inbox.

---

## 3 · Flujo de datos (2,5 min)

**Idea que debe quedar:** "al menos una vez" + idempotencia = exactamente un efecto.

**Señalar:** la línea de tiempo 1→8 y la tabla de la máquina de estados.

**Qué decir:**
- Recorrer rápido los pasos, deteniéndose en tres:
  - **Paso 2-3:** "El 202 solo sale cuando al menos dos réplicas de Kafka confirmaron. Sin 202, el emisor reintenta con el mismo idEvento y recibe 200 si ya había llegado."
  - **Paso 5-6:** "El offset de Kafka se marca **después** del commit en la base. Si el proceso muere entre los dos, se relee el evento y el inbox lo reconoce como repetido."
  - **Paso 7:** "El relay publica en orden de versión con un solo líder, para que el cliente nunca reciba 'en reparto' después de 'entregado'."
- Tabla derecha: "La máquina de estados decide qué pasa con cada evento: los tardíos y los inválidos no se pierden, quedan en el historial para auditoría."
- Dato: "una guía completa publicó sus 7 cambios en orden; el estado apareció en la consulta 391 ms después del 202."

**Preguntas probables:**
- *¿Qué pasa si llegan desordenados?* → Se compara `ocurridoEn`: si es más viejo que el último aplicado queda como `TARDIO` y no cambia el estado.
- *¿Por qué no exactamente-una-vez de Kafka (transacciones)?* → Las transacciones de Kafka no cubren la base de datos. El efecto único lo garantiza la llave primaria del inbox en la misma transacción del estado.

---

## 4 · Integraciones (1,5 min)

**Idea que debe quedar:** contratos versionados y un grupo de consumo por integración.

**Señalar:** la tabla de sistemas y el JSON del contrato.

**Qué decir:**
- "Los productores publican por la API con su propio `origen`; cada consumidor tiene su grupo y avanza a su ritmo. Si la integración documental se atrasa, no afecta al notificador."
- "Implementé el procesador y el notificador; las demás integraciones están diseñadas con su grupo y su filtro."
- "El contrato es V1 y tiene una **prueba de contrato**: si alguien cambia sin querer un campo o un código de respuesta, la compilación falla antes de romper a TMS."
- "El evento **no lleva teléfono ni correo** por la Ley 1581: el notificador los consulta en el momento de enviar."

**Preguntas probables:**
- *¿Cómo evolucionan el contrato?* → Solo cambios compatibles hacia atrás (campos opcionales). Un cambio incompatible es una V2 que convive con la V1.
- *¿Schema Registry?* → Es la evolución natural con Avro/Protobuf; en esta versión el contrato se protege con la prueba de OpenAPI.

---

## 5 · Cero pérdida (3 min) — la sección más importante

**Idea que debe quedar:** las optimizaciones pueden fallar, las garantías no; y está probado.

**Señalar las tres columnas:**
1. **Capas de idempotencia**: "Las amarillas son optimizaciones: si Redis cae, se publica igual. Las verdes son garantías: la llave primaria del inbox y la llave de notificación. Probé 50 procesamientos simultáneos del mismo evento: una sola fila."
2. **Reintentos**: "Hay dos políticas a propósito. En el procesador **el orden es sagrado**: si la base cae, espera y reintenta sin saltarse nada. En el notificador **el flujo es sagrado**: si el SMS cae, el mensaje se mueve a un tópico de reintento (1 min, 10 min, 1 h), luego va por correo, y nada se bloquea."
3. **Si Kafka se cae**: "La API envuelve a Kafka en un circuit breaker y una contingencia. Con Kafka caído seguimos respondiendo 202, y con el circuito abierto en 0,1 s. Solo si también cae PostgreSQL respondemos 503, que le dice al emisor 'reintenta'. **Nunca 202 sin durabilidad.**"
- **Evidencia (tabla de reconciliación):** "Cada fila es una prueba real: lo aceptado contra lo guardado. Diferencia cero en todas, incluida la que maté el procesador y la de la nube."

**Anécdota útil (si hay tiempo):** "Un bug real con fechas mandó 28 eventos válidos a la DLQ. Lo corregí y los **reprocesé desde Kafka** reiniciando el grupo de consumo: esa es la razón de elegir un log con replay."

**Preguntas probables:**
- *¿Y si el emisor nunca reintenta?* → El contrato del 202 es explícito: sin 202, el evento no está garantizado. La app de mensajeros es offline-first y guarda con su idEvento hasta recibir confirmación.
- *¿Qué es un poison pill?* → Un mensaje que nunca se podrá procesar (JSON roto). Va a la DLQ con motivo, partición y offset de origen para revisarlo y reinyectarlo.

---

## 6 · Escalabilidad (2,5 min)

**Idea que debe quedar:** cada pieza escala por la señal correcta, y encontré y corregí el cuello de botella midiendo.

**Señalar:** tabla izquierda y barras derechas.

**Qué decir:**
- "La API escala por CPU. Los consumidores escalan por **lag**, que es la señal real de atraso, hasta 24 instancias."
- Barras: "La prueba de carga mostró que el procesador era el cuello de botella: 90 eventos/s por instancia, porque procesaba de a uno. Lo cambié para que **cada partición tenga su propio trabajador**: el orden por guía se mantiene y las particiones avanzan en paralelo. Resultado: 343 eventos/s, ×3,8, y el atraso se drena en 2,3 minutos en vez de 12,7."
- **Honestidad (importante):** "Estas cifras son de un portátil corriendo todo a la vez. Con la API sola, la mediana es 25 ms; compitiendo con el procesador en la misma máquina sube a 229 ms. Certificar 5.000 ev/s exige infraestructura dedicada, y dejé documentado cómo hacerlo."

**Preguntas probables:**
- *¿Por qué no más particiones?* → Se pueden aumentar, pero reordenan claves; 24 da margen ×2 sobre el objetivo. Antes de eso hay palancas más baratas: micro-lotes y menos viajes a la base.
- *¿Y las lecturas del portal?* → Hoy van a PostgreSQL; a escala, una proyección en Redis detrás de la misma interfaz (CQRS).

---

## 7 · Observabilidad (2 min)

**Idea que debe quedar:** se ve el recorrido de cada evento y se alerta por SLO, no por síntomas.

**Señalar:** el diagrama de la izquierda y la tabla de alertas.

**Qué decir:**
- "Las apps solo hablan OpenTelemetry; el Collector reparte a Jaeger, Prometheus y Loki. Cambiar de proveedor es configuración."
- "Una sola traza sigue el evento por los tres servicios, **incluso cruzando el outbox**, porque guardo el contexto de la traza en la fila de la base."
- "Cada log lleva su trace_id: en Grafana, desde un error salto directo a su traza."
- "Las alertas están atadas a los SLO: las de 'página' despiertan a la guardia; las de 'ticket' no."
- Dato: "Lo verifiqué apagando Redis: las advertencias aparecieron en Loki con su trace_id y la traza existía en Jaeger."

**Si se puede, demo en vivo:** Grafana (http://localhost:3000) → tablero → fila de logs → "Ver traza en Jaeger".

**Preguntas probables:**
- *¿Por qué no solo logs?* → Los logs no miden latencias de extremo a extremo ni reconstruyen un recorrido asíncrono.

---

## 8 · Seguridad (1,5 min)

**Idea que debe quedar:** defensa en profundidad, incluida la autorización sobre los datos.

**Señalar:** las cuatro tarjetas y la tabla de verificación.

**Qué decir:**
- "Cada sistema tiene su identidad OAuth2 y su token con vigencia de 5 minutos."
- "Lo que más destaco: **cada cliente solo puede reportar su propio origen**. Si TMS intenta reportar como transporte, recibe 403; así nadie falsea el historial de una guía."
- "Ley 1581: el evento no lleva datos de contacto y los logs los enmascaran."
- "Las imágenes no tienen shell ni corren como root, y el pipeline las escanea."
- Tabla: "Todo esto lo probé contra Keycloak real, incluido un token alterado: 'la firma es inválida'."

**Preguntas probables:**
- *¿Y el API Gateway?* → Va delante con WAF y límite global; el límite por cliente de la API es una segunda defensa.

---

## 9 · Ventajas y desventajas (2 min)

**Idea que debe quedar:** las desventajas se conocen y cada una tiene mitigación.

**Qué decir:**
- "Cambiamos simplicidad por resiliencia, conscientemente, solo donde el negocio lo exige."
- Recorrer 2 o 3 filas: consistencia eventual (mitigada con el SLO de 5 s), más piezas que operar (mitigado con todo en contenedores y Kafka administrado), techo por particiones.
- "Descarté REST síncrono porque propaga las caídas, y RabbitMQ porque el orden por guía y el replay son más difíciles."
- **Riesgos conocidos:** "Cuando Kafka vuelve tras una caída, los eventos de la contingencia pueden llegar después de otros más nuevos; la regla de 'tardío' lo absorbe y está documentado como ADR."
- "Hay 12 ADR: cada decisión con su contexto y lo que descarté."

---

## 10 · De la idea a producción (2 min)

**Idea que debe quedar:** nada llega a producción sin las mismas compuertas, y el cambio se adopta sin riesgo en el pico.

**Señalar:** el pipeline de 7 etapas y las tres tarjetas.

**Qué decir:**
- "Cada cambio pasa por las 151 pruebas, incluidas pruebas contra Kafka y PostgreSQL reales; escaneo de seguridad; imágenes escaneadas; y una **prueba de humo con el sistema completo** antes de staging."
- "Producción requiere aprobación manual, despliega en canary 10 % → 100 % con rollback automático, y **del 15 de noviembre al 10 de enero el pipeline no despliega**, salvo hotfix."
- "Lo verifiqué en local y en la nube: Kafka y PostgreSQL administrados en Aiven con TLS, humo superado y 3.000 eventos sin pérdida."
- Cierre: "Si empezara en octubre, el mes 3 coincide con el pico: el objetivo realista es **modo sombra** durante el pico y la migración real en enero, un sistema a la vez."

**Frase de cierre de todo el eje:** "El éxito no es tener la tecnología más moderna, sino **un sistema aburrido en temporada pico**."

---

## Preguntas difíciles y cómo responderlas

| Pregunta | Respuesta corta |
|---|---|
| ¿Cumple los 5.000 ev/s? | Está diseñado para eso y encontré y corregí el cuello de botella midiendo, pero no está certificado: el portátil comparte CPU entre todo. Tengo el plan de certificación en infraestructura dedicada. |
| ¿No es demasiado complejo? | Cada pieza responde a un requisito: Kafka al orden y al replay, el outbox a la doble escritura, la contingencia a la caída del broker. Lo que no aportaba (event sourcing completo) lo descarté. |
| ¿Qué pasa si se cae PostgreSQL? | La ingesta sigue (Kafka guarda); el procesador espera y reintenta sin saltarse eventos; al volver, se pone al día. Probado. |
| ¿Por qué .NET si el stack es Java? | Diseñé con piezas portables (Kafka, Flyway, Resilience4j/Polly, OAuth2, OpenTelemetry): la versión Spring usa los mismos contratos, tópicos y migraciones. |
| ¿Cómo evitas notificar dos veces? | Llave guía:versión:canal y el proveedor recibe esa misma llave de idempotencia. Hice un replay completo del tópico: 0 SMS repetidos. |
| ¿Qué pasa si hay un bug en producción? | La DLQ lo aísla sin detener particiones; se corrige y se reprocesa desde Kafka (lo hice con 28 eventos reales). |
