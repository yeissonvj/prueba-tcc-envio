using Npgsql;

namespace TccEventos.Infraestructura.Postgres;

public static class FabricaBaseDatos
{
    /// La contraseña llega aparte de la cadena de conexión (variable de entorno Postgres__Contrasena):
    /// la cadena puede vivir en el repositorio, el secreto no.
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
