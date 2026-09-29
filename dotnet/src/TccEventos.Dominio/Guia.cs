using System;
using System.Collections.Generic;
using System.Text;

namespace TccEventos.Dominio;

/// <summary>
/// Una guía de envío y su estado actual (el agregado del dominio).
/// Es la única que decide si un evento cambia su estado.
/// </summary>
/// <remarks>
/// No tiene setters públicos: el estado solo cambia a través de <see cref="Aplicar(EventoGuia)"/>,
/// así las reglas del negocio no se pueden saltar desde afuera.
/// </remarks>
public class Guia
{
    /// <summary>Número único de la guía.</summary>
    public string NumeroGuia { get; }

    /// <summary>Estado en el que se encuentra la guía actualmente.</summary>
    public EstadoGuia EstadoActual { get; private set; }

    /// <summary>Momento del último evento aplicado; sirve para detectar eventos tardíos.</summary>
    public DateTimeOffset UltimoEventoEn { get; private set; }

    /// <summary>
    /// Número de cambios aplicados. Se usa para la concurrencia optimista en la base de datos
    /// y como número de orden de los cambios que se publican.
    /// </summary>
    public long Version { get; private set; }

    /// <summary>Crea la guía con todos sus datos; solo se usa desde los métodos de fábrica.</summary>
    /// <param name="numeroGuia">Número de la guía.</param>
    /// <param name="estado">Estado actual.</param>
    /// <param name="ultimoEventoEn">Momento del último evento aplicado.</param>
    /// <param name="version">Versión actual.</param>
    private Guia(string numeroGuia, EstadoGuia estado, DateTimeOffset ultimoEventoEn, long version)
    {
        NumeroGuia = numeroGuia;
        EstadoActual = estado;
        UltimoEventoEn = ultimoEventoEn;
        Version = version;
    }

    /// <summary>Crea una guía a partir del primer evento que se recibe de ella.</summary>
    /// <param name="primerEvento">Primer evento conocido de la guía; su estado se toma como el inicial.</param>
    /// <returns>Una guía nueva en versión 1.</returns>
    public static Guia Crear(EventoGuia primerEvento) =>
        new(primerEvento.NumeroGuia, primerEvento.Estado, primerEvento.OcurridoEn, version: 1);

    /// <summary>Reconstruye una guía guardada en la base de datos.</summary>
    /// <param name="numeroGuia">Número de la guía.</param>
    /// <param name="estado">Estado guardado.</param>
    /// <param name="ultimoEventoEn">Momento del último evento aplicado.</param>
    /// <param name="version">Versión guardada.</param>
    /// <returns>La guía con el estado que tenía al guardarse.</returns>
    public static Guia Reconstruir(string numeroGuia, EstadoGuia estado, DateTimeOffset ultimoEventoEn, long version) =>
        new(numeroGuia, estado, ultimoEventoEn, version);

    /// <summary>
    /// Intenta aplicar un evento a la guía y decide qué pasa con él.
    /// </summary>
    /// <param name="evento">Evento a aplicar; debe ser de esta misma guía.</param>
    /// <returns>
    /// <see cref="ResultadoAplicacion.Tardio"/> si el evento es anterior al último aplicado (el estado no retrocede);
    /// <see cref="ResultadoAplicacion.TransicionInvalida"/> si la máquina de estados no permite el cambio;
    /// <see cref="ResultadoAplicacion.Aplicado"/> si el estado cambió y la versión subió.
    /// </returns>
    /// <exception cref="ArgumentException">Si el evento pertenece a otra guía.</exception>
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
