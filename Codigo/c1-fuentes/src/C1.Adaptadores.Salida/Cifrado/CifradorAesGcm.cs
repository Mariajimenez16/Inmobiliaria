using System.Security.Cryptography;
using System.Text;

namespace C1.Adaptadores.Salida.Cifrado;

// Puerto LOCAL de este adaptador: solo lo necesita el almacen de credenciales, no el nucleo.
public interface ICifrador
{
    string Cifrar(string textoPlano);
    string Descifrar(string textoCifrado);
}

// Cifrado simetrico AES-GCM (cifra y ademas detecta alteraciones del texto).
// Formato guardado: base64( nonce(12) + etiqueta(16) + texto cifrado ).
// La llave de 256 bits se deriva del secreto de configuracion con PBKDF2.
//
// SOLID - SRP: solo cifra y descifra. DIP/OCP: se usa a traves de ICifrador, asi se puede cambiar el algoritmo sin tocar el almacen.
// Limitacion declarada: la "sal" de PBKDF2 es fija (simplicidad academica); en produccion el secreto vendria de un gestor de secretos.
public sealed class CifradorAesGcm : ICifrador
{
    private const int TamanoNonce = 12;
    private const int TamanoEtiqueta = 16;
    private readonly byte[] _clave;

    public CifradorAesGcm(string secreto)
    {
        if (string.IsNullOrWhiteSpace(secreto))
            throw new ArgumentException("El secreto de cifrado no puede estar vacio.", nameof(secreto));

        _clave = Rfc2898DeriveBytes.Pbkdf2(
            secreto, Encoding.UTF8.GetBytes("c1-hogar-andino-sal"), 100_000, HashAlgorithmName.SHA256, 32);
    }

    public string Cifrar(string textoPlano)
    {
        var nonce = RandomNumberGenerator.GetBytes(TamanoNonce); // distinto en cada cifrado
        var datos = Encoding.UTF8.GetBytes(textoPlano);
        var cifrado = new byte[datos.Length];
        var etiqueta = new byte[TamanoEtiqueta];

        using var aes = new AesGcm(_clave, TamanoEtiqueta);
        aes.Encrypt(nonce, datos, cifrado, etiqueta);

        var paquete = new byte[TamanoNonce + TamanoEtiqueta + cifrado.Length];
        nonce.CopyTo(paquete, 0);
        etiqueta.CopyTo(paquete, TamanoNonce);
        cifrado.CopyTo(paquete, TamanoNonce + TamanoEtiqueta);
        return Convert.ToBase64String(paquete);
    }

    public string Descifrar(string textoCifrado)
    {
        var paquete = Convert.FromBase64String(textoCifrado);
        if (paquete.Length < TamanoNonce + TamanoEtiqueta)
            throw new CryptographicException("El texto cifrado no es valido.");

        var nonce = paquete.AsSpan(0, TamanoNonce);
        var etiqueta = paquete.AsSpan(TamanoNonce, TamanoEtiqueta);
        var cifrado = paquete.AsSpan(TamanoNonce + TamanoEtiqueta);
        var datos = new byte[cifrado.Length];

        using var aes = new AesGcm(_clave, TamanoEtiqueta);
        aes.Decrypt(nonce, cifrado, etiqueta, datos); // lanza CryptographicException si la llave es incorrecta o el texto fue alterado
        return Encoding.UTF8.GetString(datos);
    }
}