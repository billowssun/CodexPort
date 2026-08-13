const POSTER_URL = 'https://raw.githubusercontent.com/billowssun/CodexPort/main/website/public/codexport-v1.2-poster.png?revision=multi-device';

module.exports = async function poster(request, response) {
  const source = await fetch(POSTER_URL);
  if (!source.ok) {
    response.status(502).send('Poster is temporarily unavailable.');
    return;
  }

  const image = Buffer.from(await source.arrayBuffer());
  response.setHeader('Content-Type', 'image/png');
  response.setHeader('Content-Disposition', 'inline; filename="CodexPort-v1.2-poster.png"');
  response.setHeader('Cache-Control', 'public, max-age=86400, s-maxage=604800, stale-while-revalidate=2592000');
  response.status(200).send(image);
};
