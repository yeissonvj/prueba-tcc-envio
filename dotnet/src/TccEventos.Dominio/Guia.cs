using System;
using System.Collections.Generic;
using System.Text;

namespace TccEventos.Dominio;

/// <summary>
/// Una guía de envío y su estado actual.
/// Es la única que decide si un evento cambia su estado.
/// </summary>
public class Guia
{
    public string NumeroGuia { get; }
    public EstadoGuia EstadoActual { get; private set; }
    public DateTimeOffset UltimoEventoEn { get; private set; }
    public long Version { get; private set; }

    private Guia(string numeroGuia, EstadoGuia estado, DateTimeOffset ultimoEventoEn, long version)
    {
        NumeroGuia = numeroGuia;
        EstadoActual = estado;
        UltimoEventoEn = ultimoEventoEn;
        Version = version;
    }

    /// <summary>Crea una guía a partir del primer evento que se recibe de ella.</summary>
    public static Guia Crear(EventoGuia primerEvento) =>
        new(primerEvento.NumeroGuia, primerEvento.Estado, primerEvento.OcurridoEn, version: 1);

    /// <summary>Reconstruye una guía guardada en la base de datos.</summary>
    public static Guia Reconstruir(string numeroGuia, EstadoGuia estado, DateTimeOffset ultimoEventoEn, long version) =>
        new(numeroGuia, estado, ultimoEventoEn, version);

    public ResultadoAplicacion Aplicar(EventoGuia evento)
    {
        if (evento.NumeroGuia != NumeroGuia)
            throw new ArgumentException($"El evento es de la guía {evento.NumeroGuia}, no de {NumeroGuia}.");

        if (evento.OcurridoEn < UltimoEventoEn)
            return ResultadoAplicacion.Tardio;

        if (!MaquinaEstados.PuedeTransitar(EstadoActual, evento.Estado))
            return ResultadoAplicacion.TransicionInvalida;

        EstadoActual = evento.Estado;
        UltimoEventoEn = evento.OcurridoEn;
        Version++;
        return ResultadoAplicacion.Aplicado;
    }
}