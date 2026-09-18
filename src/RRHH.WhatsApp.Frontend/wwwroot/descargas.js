// FUN-14: el archivo llega por el circuito de Blazor —que es quien tiene el token del analista— y el
// navegador lo guarda como una descarga comun. Sin esto habria que exponer la Api al navegador o
// poner un token en la URL, y el enlace de un archivo con datos personales quedaria en el historial.
window.descargarArchivo = async (nombre, flujo) => {
    const contenido = await flujo.arrayBuffer();
    const url = URL.createObjectURL(new Blob([contenido]));

    const enlace = document.createElement('a');
    enlace.href = url;
    enlace.download = nombre ?? 'archivo';

    document.body.appendChild(enlace);
    enlace.click();
    enlace.remove();

    URL.revokeObjectURL(url);
};
