/**
 * Formulario de postulacion (Regla 9): el puente entre Google Forms y la Api de RRHH.
 *
 * Cuando alguien envia el formulario, este script arma el envio y lo entrega en
 * POST /jobforms/webhook-google con el secreto compartido en la cabecera.
 *
 * Lo que NO hace es decidir: la Api valida la vacante (Regla 20), el consentimiento (Regla 17) y a
 * que postulacion pertenece el envio, por su token. Aca solo se traduce y se entrega.
 *
 * Instalacion paso a paso: README, seccion "El Apps Script del formulario".
 */

/** Titulos de las preguntas fijas. Si se renombran en el formulario, se cambian aca. */
const PREGUNTAS = {
  token: 'token',
  dni: 'DNI',
  nombre: 'Nombre completo',
  telefono: 'Telefono',
  email: 'Correo electronico',
  cv: 'CV',
  consentimiento: 'Aviso de privacidad'
};

/** Codigo de pais para un celular escrito sin el (la operacion es en Peru). */
const PREFIJO_PAIS = '+51';

/** Reintentos ante fallas pasajeras. Repetir un envio es seguro: la Api es idempotente (V27). */
const REINTENTOS = 5;
const ESPERA_INICIAL_MS = 2000;

/**
 * Tope por intento cuando la Api pide esperar con Retry-After (COR-15). Apps Script corta la
 * ejecucion de un disparador instalable a los 6 minutos y Utilities.sleep no acepta mas de 5, asi
 * que un Retry-After mas largo se recorta aca en vez de dejar que la llamada reviente sola.
 */
const ESPERA_MAXIMA_MS = 60000;

/**
 * Disparador instalable "Al enviarse el formulario". Unico punto de entrada.
 */
function alEnviarFormulario(evento) {
  entregar(armarEnvio(leerRespuestas(evento)));
}

/**
 * Comprueba la configuracion sin crear nada: manda un token que no existe y espera que la Api lo
 * rechace justamente por eso. 422 significa que la URL y el secreto estan bien; 401, que el secreto
 * no coincide. Conviene correrla una vez despues de instalar.
 */
function probarConfiguracion() {
  const respuesta = llamar({
    token: '00000000-0000-0000-0000-000000000000',
    dni: '00000000',
    consentimientoAceptado: false
  });

  const codigo = respuesta.getResponseCode();

  if (codigo === 422) {
    console.log('URL y secreto OK: la Api respondio que ese token no existe, que es lo esperado.');
    return;
  }

  if (codigo === 401) {
    throw new Error('El secreto no coincide con el de la Api (propiedad SECRETO del script).');
  }

  throw new Error('Respuesta inesperada (' + codigo + '): ' + respuesta.getContentText());
}

/** Devuelve un objeto {titulo: respuesta} con todo lo que contesto el postulante. */
function leerRespuestas(evento) {
  if (!evento || !evento.response) {
    throw new Error('El evento no trae la respuesta: el disparador tiene que ser "Al enviarse el formulario".');
  }

  const respuestas = {};

  evento.response.getItemResponses().forEach(function (item) {
    respuestas[item.getItem().getTitle().trim()] = item.getResponse();
  });

  return respuestas;
}

function armarEnvio(respuestas) {
  const token = texto(respuestas[PREGUNTAS.token]);

  if (!token) {
    // Sin token la Api no puede saber de que postulacion se trata. Pasa cuando alguien entra al
    // formulario por su cuenta en vez de por el enlace del bot, o cuando la vacante quedo cargada
    // sin el marcador {token} en su URL.
    throw new Error('El envio llego sin token: revisar el enlace prellenado cargado en la vacante.');
  }

  const dni = soloDigitos(respuestas[PREGUNTAS.dni]);

  if (!dni) {
    throw new Error('El envio llego sin DNI, que es el identificador del postulante (Regla 9).');
  }

  return {
    token: token,
    dni: dni,
    nombreCompleto: texto(respuestas[PREGUNTAS.nombre]),
    telefonoE164: normalizarTelefono(respuestas[PREGUNTAS.telefono]),
    email: texto(respuestas[PREGUNTAS.email]),
    cvUrl: enlaceDelArchivo(respuestas[PREGUNTAS.cv]),
    consentimientoAceptado: hayRespuesta(respuestas[PREGUNTAS.consentimiento]),
    datosJson: JSON.stringify(camposOpcionales(respuestas))
  };
}

/**
 * Todo lo que no es una pregunta fija son los campos opcionales que el analista configuro para esa
 * vacante (Seccion 9.2). Viajan juntos en datosJson, sin que el script tenga que conocerlos.
 */
function camposOpcionales(respuestas) {
  const fijos = Object.keys(PREGUNTAS).map(function (clave) { return PREGUNTAS[clave]; });
  const otros = {};

  Object.keys(respuestas).forEach(function (titulo) {
    if (fijos.indexOf(titulo) === -1) {
      otros[titulo] = respuestas[titulo];
    }
  });

  return otros;
}

/**
 * Google guarda los adjuntos en Drive y devuelve sus ids: lo que viaja es el enlace, no el archivo.
 * Por eso la purga de la Regla 17 puede limpiar la referencia pero no borrar el archivo en el
 * origen (ver docs/decisiones.md). Se resuelve al migrar el formulario a Razor Pages.
 */
function enlaceDelArchivo(respuesta) {
  const ids = [].concat(respuesta || []);

  if (ids.length === 0) {
    return null;
  }

  return 'https://drive.google.com/file/d/' + ids[0] + '/view';
}

function normalizarTelefono(respuesta) {
  const valor = texto(respuesta);

  if (!valor) {
    return null;
  }

  const limpio = valor.replace(/[^0-9+]/g, '');

  if (limpio.indexOf('+') === 0) {
    return limpio;
  }

  // Celular peruano escrito sin codigo de pais: nueve digitos que empiezan con 9. Cualquier otra
  // cosa se manda tal cual, sin inventarle un pais.
  if (/^9[0-9]{8}$/.test(limpio)) {
    return PREFIJO_PAIS + limpio;
  }

  return limpio;
}

function entregar(envio) {
  let espera = ESPERA_INICIAL_MS;

  for (let intento = 1; intento <= REINTENTOS; intento++) {
    const respuesta = llamar(envio);
    const codigo = respuesta.getResponseCode();

    if (codigo >= 200 && codigo < 300) {
      const cuerpo = JSON.parse(respuesta.getContentText() || '{}');

      console.log(cuerpo.yaRecibido
        ? 'La Api ya tenia este envio: el reintento no duplico nada.'
        : 'Envio entregado. Postulante ' + cuerpo.postulanteId + ', postulacion ' + cuerpo.postulacionId + '.');

      return;
    }

    // 429: el limite de velocidad propio del webhook (COR-15) no es un rechazo del negocio, es una
    // senal de que hay que esperar y volver a intentar. Va antes de la rama de rechazo 4xx a
    // proposito: si cayera en esa rama, una campana con muchos postulantes a la vez perderia
    // postulaciones por agotar los reintentos del script con un problema que se resuelve solo.
    if (codigo === 429) {
      const segundos = segundosDeRetryAfter(respuesta);
      const esperaEfectiva = Math.min(
        segundos !== null ? segundos * 1000 : espera,
        ESPERA_MAXIMA_MS
      );

      console.warn('Intento ' + intento + ' limitado por velocidad (429). Se reintenta en ' + esperaEfectiva + ' ms.');

      if (intento < REINTENTOS) {
        Utilities.sleep(esperaEfectiva);
        espera = espera * 2;
      }

      continue;
    }

    // El resto de los 4xx no se reintenta: insistir da lo mismo. 401 es el secreto mal puesto; 422,
    // un rechazo del negocio —vacante cerrada, sin consentimiento, token desconocido— que ya no
    // cambia.
    if (codigo >= 400 && codigo < 500) {
      throw new Error('La Api rechazo el envio (' + codigo + '): ' + respuesta.getContentText());
    }

    console.warn('Intento ' + intento + ' fallido (' + codigo + '). Se reintenta.');

    if (intento < REINTENTOS) {
      Utilities.sleep(Math.min(espera, ESPERA_MAXIMA_MS));
      espera = espera * 2;
    }
  }

  // Queda como ejecucion fallida: Google le avisa por correo a quien es dueno del script. Del lado
  // del sistema, el analista se entera igual por el aviso de 48h de la Regla 9.
  throw new Error('No se pudo entregar el envio despues de ' + REINTENTOS + ' intentos.');
}

/**
 * Segundos que pide esperar la cabecera Retry-After, o null si no vino o no es un numero. Se busca
 * sin distinguir mayusculas porque UrlFetchApp entrega las cabeceras tal como las mando el
 * servidor, sin garantia de que lleguen en minusculas.
 */
function segundosDeRetryAfter(respuesta) {
  const cabeceras = respuesta.getAllHeaders() || {};
  const clave = Object.keys(cabeceras).filter(function (nombre) {
    return nombre.toLowerCase() === 'retry-after';
  })[0];

  if (!clave) {
    return null;
  }

  const valor = parseInt([].concat(cabeceras[clave])[0], 10);

  return isNaN(valor) ? null : valor;
}

function llamar(envio) {
  const propiedades = PropertiesService.getScriptProperties();
  const url = propiedades.getProperty('URL_WEBHOOK');
  const secreto = propiedades.getProperty('SECRETO');

  // El secreto no vive en el codigo: va en las propiedades del script, que no viajan al copiar el
  // archivo ni quedan en el historial del repositorio.
  if (!url || !secreto) {
    throw new Error('Faltan las propiedades del script: URL_WEBHOOK y SECRETO.');
  }

  return UrlFetchApp.fetch(url, {
    method: 'post',
    contentType: 'application/json',
    headers: { 'X-JobForms-Secreto': secreto },
    payload: JSON.stringify(envio),
    muteHttpExceptions: true
  });
}

function texto(respuesta) {
  if (respuesta === null || respuesta === undefined) {
    return null;
  }

  const valor = [].concat(respuesta).join(', ').trim();

  return valor.length > 0 ? valor : null;
}

function soloDigitos(respuesta) {
  const valor = texto(respuesta);

  return valor ? valor.replace(/[^0-9]/g, '') : null;
}

function hayRespuesta(respuesta) {
  return texto(respuesta) !== null;
}
