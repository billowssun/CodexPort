const POSTER_URL = 'https://raw.githubusercontent.com/billowssun/CodexPort/main/website/public/codexport-v1.2-poster.png?revision=multi-device-v2';

module.exports = async function posterMultiDevice(request, response) {
  const source = await fetch(POSTER_URL, { cache: 'no-store' });
  if (!source.ok) {
    response.status(502).send('Poster is temporarily unavailable.');
    return;
  }

  const image = Buffer.from(await source.arrayBuffer());
  response.setHeader('Content-Type', 'image/png');
  response.setHeader('Content-Disposition', 'inline; filename="CodexPort-multi-device-poster.png"');
  response.setHeader('Cache-Control', 'public, max-age=3600, s-maxage=86400, stale-while-revalidate=604800');
  response.status(200).send(image);
};
