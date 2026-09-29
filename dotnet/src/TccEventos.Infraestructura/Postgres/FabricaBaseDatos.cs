using Npgsql;

namespace TccEventos.Infraestructura.Postgres;

/// <summary>Patrón Fábrica: crea la fuente de conexiones a PostgreSQL.</summary>
public static class FabricaBaseDatos
{
    /// <summary>
    /// Crea la fuente de conexiones. La contraseña llega aparte de la cadena de conexión (variable de entorno
    /// Postgres__Contrasena): la cadena puede vivir en el repositorio, el secreto no.
    /// </summary>
    /// <param name="opciones">Cadena de conexión y contraseña.</param>
    /// <returns>La fuente de conexiones, que se registra como singleton.</returns>
    /// <exception cref="InvalidOperationException">Si falta la cadena de conexión.</exception>
    public static NpgsqlDataSource Crear(OpcionesPostgres opciones)
    {
        if (string.IsNullOrWhiteSpace(opciones.Conexion))
            throw new InvalidOperationException("Falta la configuración 'Postgres:Conexion'.");

        var constructor = new NpgsqlDataSourceBuilder(opciones.Conexion);
        if (!string.IsNullOrEmpty(opciones.Contrasena))
            constructor.ConnectionStringBuilder.Password = opciones.Contrasena;

        return constructor.Build();
    }
}
