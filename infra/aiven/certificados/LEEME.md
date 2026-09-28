# Certificados de Aiven (no se versionan)

Descargar desde la pestaña **Overview** de cada servicio en la consola de Aiven y guardar aquí:

| Archivo | De dónde | Para qué |
|---|---|---|
| `ca.pem` | Kafka → *CA certificate* (es el mismo para todo el proyecto) | Validar el servidor de Kafka y de PostgreSQL |
| `service.cert` | Kafka → *Access certificate* | Autenticación con certificado de cliente (opción SSL) |
| `service.key` | Kafka → *Access key* | Llave del certificado de cliente (opción SSL) |

Con SASL/SCRAM solo hace falta `ca.pem`. Todo lo que hay en esta carpeta, salvo este archivo, está en `.gitignore`.
