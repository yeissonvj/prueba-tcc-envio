# ADR-0010 · Orden no garantizado mientras se vacía la contingencia

**Estado:** aceptada (riesgo conocido)

## Contexto
Cuando Kafka vuelve, el relay de contingencia publica los eventos guardados mientras los eventos nuevos ya van directo a Kafka. Durante ese intervalo, un evento viejo de una guía puede llegar al tópico después de uno nuevo de la misma guía.

## Decisión
Aceptarlo conscientemente: la ventana es corta (el relay corre cada 5 s en lotes de 500) y el procesador ya lo tolera:
- Un evento que llega después de otro más reciente se registra como `TARDIO` y no cambia el estado.
- El estado final es el del evento con `ocurridoEn` más reciente.

## Riesgo residual
Un salto que llega antes que su paso intermedio (p. ej. `ENTREGADA` antes que `EN_REPARTO`) se registra como `TRANSICION_INVALIDA`; al llegar el intermedio se aplica, y la entrega queda en el historial pero no en el estado. Se detecta con la métrica de transiciones inválidas y se corrige con un redrive del evento.

## Alternativas evaluadas
- **Enviar todo a contingencia hasta vaciarla:** preserva el orden, pero exige coordinar el estado "en recuperación" entre todas las instancias de la API.
- **Máquina de estados que acepte saltos hacia adelante:** a evaluar con producto; cambia una regla de negocio.
