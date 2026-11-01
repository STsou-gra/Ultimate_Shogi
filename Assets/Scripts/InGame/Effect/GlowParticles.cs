using System.Collections.Generic;
using UnityEngine;

// 光の粒パーティクル（プロトタイプ index.html の FX.particle の移植）
// 粒は SpriteRenderer をプールして使い回す。位置・速度は粒を置くレイヤー（Transform）のローカル座標で扱う
public class GlowParticles : MonoBehaviour
{
    private class Particle
    {
        public SpriteRenderer sprite;
        public Vector2 velocity;
        public Vector2 gravity;
        public float life;
        public float maxLife;
        public float drag;
        public float alpha;
        public float scale;
        public bool shrink;
        public Color color;
    }

    private readonly List<Particle> particles = new List<Particle>();
    private readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();
    private Sprite glowSprite;

    public void Init(Sprite sprite)
    {
        glowSprite = sprite;
    }

    // パーティクルを1つ生成する
    // drag はプロトタイプと同じく「60fps での1フレームあたりの減衰率」（1 なら減衰なし）
    public void Spawn(Transform layer, Vector2 localPos, Vector2 velocity, Color color, float life, float scale,
        int sortingOrder, Vector2 gravity = default, float drag = 0.95f, float alpha = 1f, bool shrink = true)
    {
        SpriteRenderer sr = pool.Count > 0 ? pool.Pop() : CreateSprite();
        sr.transform.SetParent(layer, false);
        sr.transform.localPosition = new Vector3(localPos.x, localPos.y, 0f);
        sr.transform.localRotation = Quaternion.identity;
        sr.transform.localScale = Vector3.one * scale;
        sr.sortingOrder = sortingOrder;
        color.a = alpha;
        sr.color = color;
        sr.gameObject.SetActive(true);

        particles.Add(new Particle
        {
            sprite = sr, velocity = velocity, gravity = gravity, life = life, maxLife = life,
            drag = drag, alpha = alpha, scale = scale, shrink = shrink, color = color,
        });
    }

    void Update()
    {
        float dt = Mathf.Min(0.05f, Time.deltaTime);
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            Particle p = particles[i];
            p.life -= dt;
            if (p.life <= 0f || p.sprite == null)
            {
                Recycle(p.sprite);
                particles.RemoveAt(i);
                continue;
            }

            float drag = Mathf.Pow(p.drag, dt * 60f);
            p.velocity = p.velocity * drag + p.gravity * dt;
            Transform t = p.sprite.transform;
            t.localPosition += (Vector3)(p.velocity * dt);

            // 消える直前にフェードアウトし、shrink なら小さくなっていく
            float k = p.life / p.maxLife;
            Color c = p.color;
            c.a = p.alpha * Mathf.Min(1f, k * 1.5f);
            p.sprite.color = c;
            t.localScale = Vector3.one * (p.scale * (p.shrink ? 0.3f + 0.7f * k : 1f));
        }
    }

    SpriteRenderer CreateSprite()
    {
        GameObject obj = new GameObject("GlowParticle");
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = glowSprite;
        return sr;
    }

    void Recycle(SpriteRenderer sr)
    {
        // 親（駒など）と一緒に破棄されていたらプールに戻さない
        if (sr == null) return;
        sr.gameObject.SetActive(false);
        sr.transform.SetParent(transform, false);
        pool.Push(sr);
    }
}
