"""Guard (diapositiva 9b): validaciones pequenas y reutilizables para proteger invariantes."""

from c2_atributos.building_blocks.errores import ErrorDeNegocio


class Guard:
    @staticmethod
    def no_vacio(valor: str | None, nombre: str, maximo: int | None = None) -> str:
        if valor is None or not str(valor).strip():
            raise ErrorDeNegocio(f"{nombre} es obligatorio.")
        limpio = str(valor).strip()
        if maximo is not None and len(limpio) > maximo:
            raise ErrorDeNegocio(f"{nombre} no puede superar {maximo} caracteres.")
        return limpio