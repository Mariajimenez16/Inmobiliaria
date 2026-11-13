namespace C1.Nucleo.Dominio;

// RAIZ DEL AGREGADO "Fuente de datos" (V6.a). Mantiene la identidad de la fuente y la relacion con sus autorizaciones.
//
// Invariantes que protege:
//   1. Cada fuente tiene una identidad estable (FuenteId, no cambia).
//   2. Cada autorizacion esta asociada a una fuente identificable y registra quien autoriza.
//   3. Solo se importan metadatos cuando existe una autorizacion VIGENTE para la fuente.
//
// SOLID - SRP: protege las reglas de UNA fuente; no sabe de bases de datos ni de HTTP.
// SOLID - OCP/DIP: no depende de nada externo; recibe la hora ("ahora") como parametro en lugar de leer el reloj.
public sealed class Fuente : RaizDeAgregado, IOriginador<MementoDeFuente>
{
    private readonly List<AutorizacionDeAcceso> _autorizaciones = new();

    private Fuente(FuenteId id, string nombre, TipoDeFuente tipo, DateTimeOffset registradaEn, DateTimeOffset? ultimaImportacionEn)
    {
        Id = id;
        Nombre = nombre;
        Tipo = tipo;
        RegistradaEn = registradaEn;
        UltimaImportacionEn = ultimaImportacionEn;
    }

    public FuenteId Id { get; }
    public string Nombre { get; }
    public TipoDeFuente Tipo { get; }
    public DateTimeOffset RegistradaEn { get; }
    public DateTimeOffset? UltimaImportacionEn { get; private set; }
    public IReadOnlyList<AutorizacionDeAcceso> Autorizaciones => _autorizaciones;

    public AutorizacionDeAcceso? AutorizacionVigente => _autorizaciones.FirstOrDefault(a => a.Vigente);

    // "Una fuente identificada pero sin inventariar se muestra como area sin cubrir" (glosario V7).
    public bool SinCubrir => UltimaImportacionEn is null;

    // ---- Comando: Registrar fuente -> evento Fuente registrada
    public static Fuente Registrar(string nombre, TipoDeFuente tipo, DateTimeOffset ahora)
    {
        if (!Enum.IsDefined(tipo))
            throw new ErrorDeValidacion("El tipo de fuente no es valido.");

        var fuente = new Fuente(FuenteId.Nueva(), Guarda.TextoObligatorio(nombre, "nombre"), tipo, ahora, null);
        fuente.AgregarEvento(new FuenteRegistrada(Guid.NewGuid(), ahora, fuente.Id, fuente.Nombre, fuente.Tipo));
        return fuente;
    }

    // ---- Comando: Autorizar acceso a metadatos -> evento Acceso a metadatos autorizado
    public AutorizacionDeAcceso AutorizarAcceso(string autorizador, DateTimeOffset ahora)
    {
        var quien = Guarda.TextoObligatorio(autorizador, "autorizador");

        if (AutorizacionVigente is not null)
            throw new ErrorDeConflicto("La fuente ya tiene una autorizacion de acceso vigente.");

        var autorizacion = AutorizacionDeAcceso.Otorgar(Id, quien, ahora);
        _autorizaciones.Add(autorizacion);
        AgregarEvento(new AccesoAMetadatosAutorizado(Guid.NewGuid(), ahora, Id, autorizacion.Id, autorizacion.Autorizador));
        return autorizacion;
    }

    // ---- Comando: Revocar autorizacion de metadatos -> evento Autorizacion de metadatos revocada
    public void RevocarAutorizacion(DateTimeOffset ahora)
    {
        var vigente = AutorizacionVigente
            ?? throw new ErrorDeReglaDeNegocio("La fuente no tiene una autorizacion vigente que revocar.");

        vigente.Revocar(ahora);
        AgregarEvento(new AutorizacionDeMetadatosRevocada(Guid.NewGuid(), ahora, Id, vigente.Id));
    }

    // Invariante 3: sin autorizacion vigente no se importa. La capa de aplicacion lo llama ANTES de leer la fuente externa.
    public void VerificarImportacionPermitida()
    {
        if (AutorizacionVigente is null)
            throw new ErrorDeReglaDeNegocio(
                "No se pueden importar metadatos: la fuente no tiene una autorizacion de acceso vigente.");
    }

    // ---- Comando: Importar metadatos tecnicos -> evento Metadatos tecnicos importados
    // La version de estructura y sus metadatos viven FUERA del agregado (V6.a); aqui solo se deja constancia del hecho.
    public void RegistrarImportacion(Guid versionId, int numeroDeVersion, int cantidadDeColumnas, DateTimeOffset ahora)
    {
        VerificarImportacionPermitida();
        UltimaImportacionEn = ahora;
        AgregarEvento(new MetadatosTecnicosImportados(Guid.NewGuid(), ahora, Id, versionId, numeroDeVersion, cantidadDeColumnas));
    }

    // ---- Patron Memento: la fuente es el ORIGINADOR.
    public MementoDeFuente CrearMemento() => new(
        Id,
        UltimaImportacionEn,
        _autorizaciones
            .Select(a => new AutorizacionGuardada(a.Id, a.FuenteId, a.Autorizador, a.AutorizadaEn, a.RevocadaEn))
            .ToArray(),
        Eventos.Count);

    public void Restaurar(MementoDeFuente memento)
    {
        if (memento.FuenteId != Id)
            throw new ErrorDeReglaDeNegocio("El memento pertenece a otra fuente.");

        UltimaImportacionEn = memento.UltimaImportacionEn;
        _autorizaciones.Clear();
        _autorizaciones.AddRange(memento.Autorizaciones.Select(a =>
            AutorizacionDeAcceso.Reconstituir(a.Id, a.FuenteId, a.Autorizador, a.AutorizadaEn, a.RevocadaEn)));
        DescartarEventosDesde(memento.EventosPendientes);
    }

    // Para que el adaptador de persistencia reconstruya una fuente ya guardada (no levanta eventos).
    public static Fuente Reconstituir(
        FuenteId id, string nombre, TipoDeFuente tipo, DateTimeOffset registradaEn,
        DateTimeOffset? ultimaImportacionEn, IEnumerable<AutorizacionDeAcceso> autorizaciones)
    {
        var fuente = new Fuente(id, nombre, tipo, registradaEn, ultimaImportacionEn);
        fuente._autorizaciones.AddRange(autorizaciones);
        return fuente;
    }
}