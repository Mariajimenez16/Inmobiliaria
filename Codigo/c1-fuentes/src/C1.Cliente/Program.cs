using System.Text;
using C1.Cliente;

// Raiz de composicion del cliente: crea las piezas y las conecta.
//   C1_URL  direccion de C1 (por defecto http://localhost:8011)
//   C2_URL  direccion de C2 (por defecto http://localhost:8012), solo la usa la demostracion C1 -> C2
Console.OutputEncoding = Encoding.UTF8;

var c1 = new ClienteHttp(Environment.GetEnvironmentVariable("C1_URL") ?? "http://localhost:8011");
var c2 = new ClienteHttp(Environment.GetEnvironmentVariable("C2_URL") ?? "http://localhost:8012");

var consola = new ConsolaReal();
var presentador = new Presentador(consola);
var demostraciones = new DemostracionGuiada(c1, c2, presentador, consola);

await new MenuDeC1(c1, presentador, consola, demostraciones).Ejecutar();